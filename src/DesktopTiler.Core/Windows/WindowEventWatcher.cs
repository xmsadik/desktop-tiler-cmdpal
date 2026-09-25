using System;
using System.Runtime.InteropServices;
using System.Threading;
using DesktopTiler.Core.VirtualDesktops;

namespace DesktopTiler.Core.Windows;

/// <summary>
/// Raises <see cref="Changed"/> (debounced) when a top-level window may have appeared,
/// disappeared, or moved to another virtual desktop - i.e. when a desktop's "has app windows"
/// state may have changed. Built on out-of-context WinEvent hooks for show/hide/destroy and
/// cloak/uncloak (the shell cloaks a window when it moves off the current desktop), not on
/// polling.
///
/// Out-of-context hook callbacks are delivered through the message queue of the thread that
/// installed the hook, so a dedicated background thread installs both hooks and then just pumps
/// <c>GetMessage</c> until <see cref="Dispose"/> posts it WM_QUIT.
/// </summary>
public sealed class WindowEventWatcher : IDisposable
{
    /// <summary>Burst window: opening one app typically produces several show/cloak events.</summary>
    public static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);

    // The hook callback must be a static [UnmanagedCallersOnly] method (trim/AOT-safe, no
    // delegate marshalling). It always runs on the hook thread, so a [ThreadStatic] is enough to
    // route it back to the owning instance.
    [ThreadStatic]
    private static WindowEventWatcher? t_current;

    private readonly Debouncer _debouncer;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private uint _threadId;
    private int _disposed;

    public WindowEventWatcher()
    {
        // Debouncer runs its callback under its own lock, and Signal() (called on the hook
        // thread) takes that same lock - so hand the (potentially slow: a full window scan plus a
        // COM round-trip) Changed handlers off to the thread pool rather than letting them stall
        // this thread's message pump, and with it WinEvent delivery and Dispose().
        _debouncer = new Debouncer(DebounceDelay, () => ThreadPool.QueueUserWorkItem(static w => w.Changed?.Invoke(w, EventArgs.Empty), this, preferLocal: false));
        _thread = new Thread(Run) { IsBackground = true, Name = "DesktopTiler.WindowEventWatcher" };
        _thread.Start();
        _ready.Wait();
    }

    /// <summary>Raised on a thread-pool thread, at most once per <see cref="DebounceDelay"/> burst.</summary>
    public event EventHandler? Changed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_threadId != 0)
        {
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WmQuit, 0, 0);
            _thread.Join();
        }

        _debouncer.Dispose();
        _ready.Dispose();
    }

    private unsafe void Run()
    {
        t_current = this;

        // Force this thread's message queue into existence before publishing its id, so a
        // Dispose() racing construction can't PostThreadMessage into a queue that doesn't exist.
        NativeMethods.PeekMessage(out _, 0, 0, 0, NativeMethods.PmNoRemove);

        const uint flags = NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess;
        var showHideDestroy = NativeMethods.SetWinEventHook(
            NativeMethods.EventObjectDestroy, NativeMethods.EventObjectHide, 0, &OnWinEvent, 0, 0, flags);
        var cloak = NativeMethods.SetWinEventHook(
            NativeMethods.EventObjectCloaked, NativeMethods.EventObjectUncloaked, 0, &OnWinEvent, 0, 0, flags);

        _threadId = NativeMethods.GetCurrentThreadId();
        _ready.Set();

        try
        {
            // GetMessage returns 0 on WM_QUIT and -1 on error; stop on either.
            while (NativeMethods.GetMessage(out _, 0, 0, 0) > 0)
            {
            }
        }
        finally
        {
            // UnhookWinEvent must be called from the thread that installed the hook.
            if (showHideDestroy != 0)
            {
                NativeMethods.UnhookWinEvent(showHideDestroy);
            }

            if (cloak != 0)
            {
                NativeMethods.UnhookWinEvent(cloak);
            }

            t_current = null;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        try
        {
            if (idObject != NativeMethods.ObjIdWindow || idChild != 0 || !IsRelevantWindow(hwnd))
            {
                return;
            }

            t_current?._debouncer.Signal();
        }
        catch (Exception)
        {
            // An exception must never unwind out of an [UnmanagedCallersOnly] callback (it would
            // terminate the process); a missed refresh is harmless.
        }
    }

    /// <summary>Cheap pre-filter so tooltips, menus and child windows (which show/hide
    /// constantly) don't trigger a desktop-occupancy re-scan. A window that is already gone
    /// (typical for DESTROY, which arrives asynchronously) can't be inspected, so it counts.</summary>
    private static bool IsRelevantWindow(nint hwnd)
    {
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        if (root == 0)
        {
            return true;
        }

        if (root != hwnd)
        {
            return false;
        }

        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        return (exStyle & NativeMethods.WsExToolWindow) == 0
            && NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner) == 0;
    }
}
