using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>
/// Client for the undocumented virtual-desktop COM interfaces plus the public, documented
/// IVirtualDesktopManager. Every COM call happens on one dedicated, long-lived STA thread (COM
/// RCWs/CCWs are apartment-affine and must not be called cross-thread); public members post work
/// to that thread and return a <see cref="Task"/> that completes when the call finishes.
///
/// Connecting never happens in the constructor: it's deferred to the first call that needs it,
/// lazily, on the STA thread (see <see cref="EnsureInternalConnected"/> and
/// <see cref="EnsurePublicConnected"/>), because at construction time - e.g. login, before
/// Explorer has finished starting - CoCreateInstance can fail transiently
/// (RPC_S_SERVER_UNAVAILABLE, REGDB_E_CLASSNOTREG, CO_E_SERVER_EXEC_FAILURE - see
/// <see cref="ThrowIfConnectFailed"/>) even on a fully supported build; a constructor that waited
/// for and rethrew that failure would take the whole extension down with it (Program.Main never
/// reaches RegisterClass). A transient connect failure is simply retried on the next call, since
/// the internal manager is left null. Only E_NOINTERFACE from the QueryService call that follows a
/// successful ImmersiveShell activation - proving Explorer is up and serving, so a genuine
/// QueryInterface mismatch really does mean this Windows build's virtual-desktop COM layout
/// doesn't match what Phase 1 was built against - is permanent, not transient: that one is cached
/// (<see cref="VdConnectionState"/>) and every later call throws the same
/// <see cref="UnsupportedBuildException"/> without attempting to reconnect. The public,
/// documented IVirtualDesktopManager is connected independently of the internal one, so
/// <see cref="IsWindowOnCurrentVirtualDesktopAsync"/> (and therefore tiling) keeps working even
/// when the internal manager is unsupported.
///
/// Once connected, an already-live call additionally reconnects and retries once (see
/// <see cref="ComRetry"/>), transparently, if it fails with RPC_S_SERVER_UNAVAILABLE,
/// RPC_E_DISCONNECTED, REGDB_E_CLASSNOTREG, or CO_E_SERVER_EXEC_FAILURE (Explorer restarted) - a
/// second failure propagates.
///
/// Every call is additionally bounded so a wedged STA thread (stuck in one hung native COM call)
/// can't block a caller indefinitely or silently pile up a backlog of closures that would run
/// later against stale state: see <see cref="QueueTimeoutPolicy"/>, used from <see cref="RunAsync{T}"/>.
/// </summary>
public sealed class VdComClient : IDisposable
{
    private const int ENointerface = unchecked((int)0x80004002);
    private const int RegDbEClassNotReg = unchecked((int)0x80040154);
    private const int CoEServerExecFailure = unchecked((int)0x80080005);
    private const int RpcServerUnavailable = unchecked((int)0x800706BA);
    private const int RpcEDisconnected = unchecked((int)0x80010108);

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly VdConnectionState _connectionState = new();

    private static readonly StrategyBasedComWrappers ComWrappers = new();

    private ExceptionDispatchInfo? _initError;
    private IVirtualDesktopManagerInternal? _vdmi;
    private IVirtualDesktopManager? _publicVdm;
    private volatile bool _useAnimation = true;
    private int _disposed;

    // 0 = idle; otherwise Environment.TickCount64 when the action currently executing on the STA
    // thread started. Written only by that thread (around each queued action - see RunAsync);
    // read by any thread via RunAsync's fail-fast check. Interlocked, not volatile: it's a long,
    // which isn't atomically read/written without it.
    private long _currentActionStartTicks;

    public VdComClient()
    {
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "DesktopTiler-VD-STA",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        // Only waits for CoInitializeEx (the STA thread itself coming up) - never for a connect
        // attempt, which is deferred to the first call. A CoInitializeEx failure is a broken COM
        // subsystem, not a virtual-desktop-specific problem, and is truly fatal - there's nothing
        // this client (or any COM-based feature) could do on this thread regardless.
        _ready.Wait();
        _initError?.Throw();
    }

    /// <summary>Whether the switch path animates: <see langword="true"/> (the default) uses
    /// <c>SwitchDesktopWithAnimation</c>, falling back to the plain <c>SwitchDesktop</c> if that
    /// fails or is unavailable (see <see cref="SwitchCore"/>); <see langword="false"/> calls
    /// <c>SwitchDesktop</c> directly. Backs the "Switch animation" setting - the provider sets it
    /// at startup and again whenever that setting changes. Written from whatever thread raises the
    /// settings-changed event and read on this client's dedicated STA thread, hence
    /// <see langword="volatile"/> rather than a lock: a plain bool needs no more than that for safe
    /// cross-thread visibility.</summary>
    public bool UseAnimation
    {
        get => _useAnimation;
        set => _useAnimation = value;
    }

    public Task<IReadOnlyList<Guid>> GetDesktopIdsAsync() => RunAsync(GetDesktopIdsCore);

    public Task<Guid> GetCurrentIdAsync() => RunAsync(GetCurrentIdCore);

    public Task SwitchAsync(Guid id, bool animate) => RunAsync(() =>
    {
        SwitchCore(id, animate);
        return true;
    });

    public Task<bool> IsWindowOnCurrentVirtualDesktopAsync(nint hwnd) => RunAsync(() => IsOnCurrentCore(hwnd));

    public void Dispose()
    {
        // Interlocked, not "if (_queue.IsAddingCompleted)": two racing Dispose() calls must not
        // both proceed past this point (which would double-Dispose _queue/_ready below).
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.CompleteAdding();
        if (_thread.Join(TimeSpan.FromSeconds(5)))
        {
            _queue.Dispose();
        }
        else
        {
            // The STA thread is stuck - most likely a hung COM call. Disposing _queue now would
            // race its still-running GetConsumingEnumerable() in ThreadMain and throw
            // ObjectDisposedException on that background thread, crashing the process. Leak the
            // queue instead (the thread is a background thread; the process is presumably
            // shutting down or idling this extension out regardless).
            Trace.TraceWarning(
                "VdComClient.Dispose: STA thread did not exit within 5s (likely a hung COM call); leaking its work queue instead of risking a background-thread crash.");
        }

        _ready.Dispose();
    }

    private void ThreadMain()
    {
        try
        {
            var hr = Ole32.CoInitializeEx(0, Ole32.CoinitApartmentThreaded);
            if (hr < 0)
            {
                throw new InvalidOperationException($"CoInitializeEx failed: 0x{hr:X8}");
            }
        }
        catch (Exception ex)
        {
            _initError = ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            _ready.Set();
        }

        try
        {
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                action();
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // Defense in depth alongside the "leak instead of dispose" logic in Dispose(): if the
            // queue is ever disposed while this loop is still consuming it, exit cleanly instead
            // of letting the exception escape on this background thread and crash the process.
            Trace.TraceWarning($"VdComClient STA thread: queue enumeration ended abnormally: {ex.Message}");
        }
        finally
        {
            // Still on this STA thread, so still the right (only safe) place to release whatever
            // is currently connected - see ReleaseConnections. Best-effort: a release failing here
            // must not stop CoUninitialize from running.
            try
            {
                ReleaseConnections();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"VdComClient STA thread: releasing connections failed: {ex.Message}");
            }

            Ole32.CoUninitialize();
        }
    }

    /// <summary>Connects the internal (undocumented) manager if it isn't already, on this STA
    /// thread, called lazily from every Core method that needs <see cref="_vdmi"/>. Short-circuits
    /// on the cached sticky failure (see <see cref="VdConnectionState"/>) instead of repeating a
    /// doomed QueryService call.
    ///
    /// CLSID_ImmersiveShell has no LocalServer32 - Explorer registers its class object at runtime,
    /// only once it's finished starting - so CoCreateInstance here failing with
    /// REGDB_E_CLASSNOTREG (or CO_E_SERVER_EXEC_FAILURE) just means Explorer isn't up yet (login,
    /// or it crashed and is restarting), not an unsupported build: that failure (like any other
    /// from this call) is transient and uncached, leaving <see cref="_vdmi"/> null so the next
    /// call tries again. Only E_NOINTERFACE from the QueryService call below latches
    /// <see cref="UnsupportedBuildException"/> permanently - and only there, because reaching that
    /// call means CoCreateInstance just succeeded, proving Explorer is up and serving this
    /// process, so a QueryInterface mismatch really does mean this build's virtual-desktop COM
    /// layout doesn't match what Phase 1 was built against.</summary>
    private void EnsureInternalConnected()
    {
        if (_vdmi is not null)
        {
            return;
        }

        if (_connectionState.IsUnsupported)
        {
            throw _connectionState.Sticky;
        }

        var hr = Ole32.CoCreateInstance(VdGuids.ClsidImmersiveShell, 0, Ole32.ClsctxServer, typeof(IServiceProvider10).GUID, out var shellPtr);
        ThrowIfConnectFailed(hr, "CoCreateInstance(ImmersiveShell)");
        var shell = WrapAndRelease<IServiceProvider10>(shellPtr);

        hr = shell.QueryService(VdGuids.ClsidVirtualDesktopManagerInternal, typeof(IVirtualDesktopManagerInternal).GUID, out var vdmi);
        if (hr == ENointerface)
        {
            var unsupported = new UnsupportedBuildException(
                $"QueryService(IVirtualDesktopManagerInternal) failed with 0x{hr:X8}. This " +
                "Windows build's virtual-desktop COM layout is not supported.");
            _connectionState.RecordFailure(unsupported);
            throw unsupported;
        }

        ThrowIfConnectFailed(hr, "QueryService(IVirtualDesktopManagerInternal)");
        _vdmi = vdmi ?? throw new InvalidOperationException("QueryService returned S_OK but a null IVirtualDesktopManagerInternal.");
    }

    /// <summary>Connects the public, documented IVirtualDesktopManager if it isn't already, on this
    /// STA thread, called lazily from every Core method that needs <see cref="_publicVdm"/> -
    /// independently of <see cref="EnsureInternalConnected"/>, so
    /// <see cref="IsWindowOnCurrentVirtualDesktopAsync"/> (and therefore tiling) keeps working even
    /// when the internal manager is unsupported on this build. Uses
    /// <see cref="ThrowIfConnectFailed"/>, not <see cref="ThrowIfFailed"/>, for the same reason as
    /// <see cref="EnsureInternalConnected"/>: a CoCreateInstance failure here is never latched or
    /// reported as an unsupported build, only ever transient (retried on the next call).</summary>
    private void EnsurePublicConnected()
    {
        if (_publicVdm is not null)
        {
            return;
        }

        var hr = Ole32.CoCreateInstance(VdGuids.ClsidVirtualDesktopManager, 0, Ole32.ClsctxServer, typeof(IVirtualDesktopManager).GUID, out var pubPtr);
        ThrowIfConnectFailed(hr, "CoCreateInstance(VirtualDesktopManager)");
        _publicVdm = WrapAndRelease<IVirtualDesktopManager>(pubPtr);
    }

    private IReadOnlyList<Guid> GetDesktopIdsCore()
    {
        EnsureInternalConnected();

        var ids = new List<Guid>();
        foreach (var (desktop, id) in EnumerateDesktops())
        {
            _ = desktop;
            ids.Add(id);
        }

        return ids;
    }

    private Guid GetCurrentIdCore()
    {
        EnsureInternalConnected();

        var hr = _vdmi!.GetCurrentDesktop(out var current);
        ThrowIfFailed(hr, "GetCurrentDesktop");
        hr = current!.GetId(out var id);
        ThrowIfFailed(hr, "IVirtualDesktop.GetId");
        return id;
    }

    private void SwitchCore(Guid id, bool animate)
    {
        EnsureInternalConnected();

        var target = FindDesktop(id);

        // Both gates must allow it: the caller's own animate argument (every current call site
        // passes true, i.e. "animate if the user wants animation") AND the user's "Switch
        // animation" setting. Either one being false means a plain, non-animated switch.
        if (animate && UseAnimation)
        {
            var hr = _vdmi!.SwitchDesktopWithAnimation(target);
            if (hr >= 0)
            {
                hr = _vdmi.WaitForAnimationToComplete();
                ThrowIfFailed(hr, "WaitForAnimationToComplete");
                return;
            }

            if (IsReconnectable(hr) || hr == ENointerface)
            {
                // A COM/RPC problem or an unsupported build - let the caller's reconnect/retry
                // (ComRetry) or UnsupportedBuildException handling deal with it. Don't mask it
                // behind a silent fallback to the non-animated switch.
                ThrowIfFailed(hr, "SwitchDesktopWithAnimation");
            }

            // Anything else (e.g. desktop animations turned off in Windows settings, or the
            // method genuinely unavailable) - fall back to the non-animated switch per the
            // Phase 1 spec.
        }

        var switchHr = _vdmi!.SwitchDesktop(target);
        ThrowIfFailed(switchHr, "SwitchDesktop");
    }

    private IVirtualDesktop FindDesktop(Guid id)
    {
        foreach (var (desktop, desktopId) in EnumerateDesktops())
        {
            if (desktopId == id)
            {
                return desktop;
            }
        }

        throw new InvalidOperationException($"Virtual desktop {id} was not found.");
    }

    private bool IsOnCurrentCore(nint hwnd)
    {
        EnsurePublicConnected();

        var hr = _publicVdm!.IsWindowOnCurrentVirtualDesktop(hwnd, out var onCurrent);
        ThrowIfFailed(hr, "IsWindowOnCurrentVirtualDesktop");
        return onCurrent;
    }

    private IEnumerable<(IVirtualDesktop Desktop, Guid Id)> EnumerateDesktops()
    {
        var hr = _vdmi!.GetDesktops(out var arr);
        ThrowIfFailed(hr, "GetDesktops");
        hr = arr!.GetCount(out var count);
        ThrowIfFailed(hr, "IObjectArray.GetCount");

        var ivdIid = typeof(IVirtualDesktop).GUID;
        for (var i = 0; i < count; i++)
        {
            hr = arr.GetAt(i, ivdIid, out var desktop);
            ThrowIfFailed(hr, "IObjectArray.GetAt");
            hr = desktop!.GetId(out var id);
            ThrowIfFailed(hr, "IVirtualDesktop.GetId");
            yield return (desktop!, id);
        }
    }

    private static readonly QueueTimeoutPolicy TimeoutPolicy = new(TaskTimeoutExtensions.DefaultTimeout);

    private Task<T> RunAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Fail fast: every caller bounds its wait with WaitOrTimeout(DefaultTimeout), so if the STA
        // thread's currently-executing action has already run longer than that, it's wedged in a
        // hung COM call (Explorer not responding) - this call would just wait behind it, and its
        // caller has already given up on waits that long. Complete immediately instead of joining
        // the queue at all, so a wedged STA thread can't accumulate an ever-growing backlog of
        // closures that would each eventually run - possibly against stale state - if the hang
        // ever clears.
        var runningSince = Interlocked.Read(ref _currentActionStartTicks);
        if (TimeoutPolicy.ShouldFailFast(runningSince, Environment.TickCount64))
        {
            tcs.SetException(new ExplorerNotRespondingException(ExplorerNotRespondingException.DefaultMessage));
            return tcs.Task;
        }

        // Belt-and-braces for the same backlog scenario: even an item that got queued before the
        // check above tripped (or while the current action was still within its own timeout) can
        // still be sitting behind other work by the time it's finally dequeued. Each item carries
        // its own deadline and is faulted, not run, if that deadline has already passed.
        var deadline = TimeoutPolicy.DeadlineFrom(Environment.TickCount64);

        try
        {
            _queue.Add(() =>
            {
                if (QueueTimeoutPolicy.HasExpired(deadline, Environment.TickCount64))
                {
                    tcs.SetException(new ExplorerNotRespondingException(ExplorerNotRespondingException.DefaultMessage));
                    return;
                }

                Interlocked.Exchange(ref _currentActionStartTicks, Environment.TickCount64);
                try
                {
                    tcs.SetResult(ExecuteWithReconnect(work));
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _currentActionStartTicks, 0);
                }
            });
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding() already called - the client is disposed.
            tcs.SetException(new ObjectDisposedException(nameof(VdComClient)));
        }

        var task = tcs.Task;

        // A caller that gave up via WaitOrTimeout never looks at this task again. If it later
        // faults, nobody observes the exception, and .NET raises TaskScheduler.UnobservedTaskException
        // when the task is finalized (historically fatal, and noisy even where it isn't). Touching
        // Task.Exception marks it observed; this continuation does nothing else and never itself
        // faults, so it can't cause the same problem one level up.
        task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return task;
    }

    // Reconnect + retry is a per-call policy (see ComRetry), not a "once ever" flag: if Explorer
    // restarts twice over the life of this client, each restart gets its own reconnect attempt.
    private T ExecuteWithReconnect<T>(Func<T> work)
    {
        _initError?.Throw();

        return ComRetry.Run(
            work,
            ResetConnections,
            static ex => ex is COMException comEx && IsReconnectable(comEx.HResult));
    }

    /// <summary>The "reconnect" half of <see cref="ComRetry"/>'s per-call retry, for a connection
    /// that was live but just failed with RPC_S_SERVER_UNAVAILABLE/RPC_E_DISCONNECTED (Explorer
    /// restarted): releases and drops both COM references so the retried call's own
    /// <see cref="EnsureInternalConnected"/>/<see cref="EnsurePublicConnected"/> - whichever it
    /// needs - reconnects lazily instead of duplicating that logic here. Always runs on the STA
    /// thread (called from inside the queued closure that owns these objects), same as
    /// <see cref="ReleaseConnections"/>.</summary>
    private void ResetConnections() => ReleaseConnections();

    /// <summary>Releases the native COM reference each connected manager holds, then drops it.
    /// <see cref="_vdmi"/>/<see cref="_publicVdm"/> are RCWs produced by
    /// <see cref="ComWrappers"/>/source-generated COM interop (<c>[GeneratedComInterface]</c>), not
    /// classic COM interop - <see cref="Marshal.ReleaseComObject"/> only applies to the latter and
    /// would throw <see cref="ArgumentException"/> here. Source-generated RCWs are
    /// <see cref="ComObject"/> instances (they do NOT implement <see cref="IDisposable"/>), and
    /// <see cref="ComObject.FinalRelease"/> releases the underlying AddRef'd pointer
    /// deterministically instead of waiting for finalization (which would run on
    /// the finalizer thread, not this STA thread, and COM objects are apartment-affine). Must only
    /// be called on this client's own STA thread, same as every other COM call.</summary>
    private void ReleaseConnections()
    {
        ((object?)_vdmi as ComObject)?.FinalRelease();
        ((object?)_publicVdm as ComObject)?.FinalRelease();
        _vdmi = null;
        _publicVdm = null;
    }

    // REGDB_E_CLASSNOTREG/CO_E_SERVER_EXEC_FAILURE are included alongside the RPC codes: a connect
    // attempt racing Explorer's own startup (or a crash/restart) can hit either, and both are just
    // as transient - see EnsureInternalConnected/EnsurePublicConnected.
    private static bool IsReconnectable(int hr) =>
        hr is RpcServerUnavailable or RpcEDisconnected or RegDbEClassNotReg or CoEServerExecFailure;

    private static void ThrowIfFailed(int hr, string context)
    {
        if (hr >= 0)
        {
            return;
        }

        if (hr == ENointerface || hr == RegDbEClassNotReg)
        {
            throw new UnsupportedBuildException(
                $"{context} failed with 0x{hr:X8}. This Windows build's virtual-desktop " +
                "COM layout is not supported.");
        }

#pragma warning disable CA2201 // COMException is exactly the right type for a raw HRESULT failure; callers pattern-match on it.
        throw new COMException($"{context} failed", hr);
#pragma warning restore CA2201
    }

    /// <summary>Like <see cref="ThrowIfFailed"/> but for the two connect call sites
    /// (<see cref="EnsureInternalConnected"/>, <see cref="EnsurePublicConnected"/>) only: never
    /// converts a failure into <see cref="UnsupportedBuildException"/>. Neither CLSID here has a
    /// LocalServer32 registered ahead of time - both are served by Explorer at runtime - so
    /// REGDB_E_CLASSNOTREG/CO_E_SERVER_EXEC_FAILURE (and any other HRESULT) at this step just means
    /// "not up yet", always transient, never a build-support signal.</summary>
    private static void ThrowIfConnectFailed(int hr, string context)
    {
        if (hr >= 0)
        {
            return;
        }

#pragma warning disable CA2201 // COMException is exactly the right type for a raw HRESULT failure; callers pattern-match on it.
        throw new COMException($"{context} failed", hr);
#pragma warning restore CA2201
    }

    private static T WrapAndRelease<T>(nint ppv)
        where T : class
    {
        if (ppv == 0)
        {
            throw new InvalidOperationException("Received a null interface pointer.");
        }

        try
        {
            return (T)ComWrappers.GetOrCreateObjectForComInstance(ppv, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(ppv);
        }
    }
}
