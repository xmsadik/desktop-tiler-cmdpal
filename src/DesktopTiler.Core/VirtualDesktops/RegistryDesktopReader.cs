using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>advapi32.dll entry point for the registry change watcher, top-level so the
/// [LibraryImport] source generator doesn't need <see cref="RegistryDesktopReader"/> itself to
/// be partial.</summary>
internal static partial class Advapi32
{
    [LibraryImport("advapi32.dll")]
    internal static partial int RegNotifyChangeKeyValue(
        SafeRegistryHandle hKey,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        int dwNotifyFilter,
        SafeWaitHandle hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);
}

/// <summary>
/// Reads the ordered list of virtual desktops (and their optional names) from
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops</c>, and watches
/// that key for changes via <c>RegNotifyChangeKeyValue</c> (thread-agnostic, re-armed before every
/// <see cref="Changed"/> notification so nothing is missed - no polling once the key exists).
/// Bursts of writes (Explorer touches several values per switch/rename) are coalesced into a
/// single <see cref="Changed"/> event via <see cref="Debouncer"/>.
/// </summary>
public sealed class RegistryDesktopReader : IDisposable
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private const string ValueName = "VirtualDesktopIDs";

    private const int RegNotifyChangeName = 0x00000001;
    private const int RegNotifyChangeLastSet = 0x00000004;
    private const int RegNotifyThreadAgnostic = 0x10000000;
    private const int NotifyFilter = RegNotifyChangeName | RegNotifyChangeLastSet | RegNotifyThreadAgnostic;

    // Explorer writes several values (the id list, then per-desktop names) for a single logical
    // change (a switch, a rename, an add/remove), each of which signals the watch independently.
    // Coalesce a burst into one Changed event instead of firing once per write.
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(75);

    private readonly AutoResetEvent _changeEvent = new(false);

    /// <summary>Failure diagnostics hook, wired to SpikeLog by the app (Core can't reference it).
    /// Note: the watcher only receives notifications because the package manifest declares
    /// <c>unvirtualizedResources</c> - under MSIX registry virtualization reads of this key still
    /// work but RegNotifyChangeKeyValue never fires for Explorer's writes.</summary>
    public static Action<string>? Log { get; set; }
    private readonly Debouncer _debouncer;
    private readonly Thread _watchThread;

    // Guards RaiseChanged() against Dispose(): without it, a debounce-timer callback already past
    // its "am I disposed" check could still invoke Changed after Dispose() has returned (Timer/
    // ITimer.Dispose() doesn't wait for an in-flight callback). Taking this lock in Dispose()
    // around the final teardown blocks until any such in-flight RaiseChanged() call completes.
    private readonly object _raiseGate = new();
    private volatile bool _disposed;

    public RegistryDesktopReader()
    {
        _debouncer = new Debouncer(DebounceWindow, RaiseChanged);
        _watchThread = new Thread(WatchLoop)
        {
            IsBackground = true,
            Name = "DesktopTiler-RegistryWatch",
        };
        _watchThread.Start();
    }

    /// <summary>Raised (on the background watch thread) whenever the VirtualDesktops key or a
    /// desktop name subkey changes. Consumers should re-read via <see cref="ReadDesktops"/>.</summary>
    public event EventHandler? Changed;

#pragma warning disable CA1822 // Instance method by design (RegistryDesktopReader's primary read API), even though it doesn't touch instance state today.
    public IReadOnlyList<DesktopInfo> ReadDesktops()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key is null)
        {
            return [];
        }

        var raw = key.GetValue(ValueName) as byte[];
        var ids = VirtualDesktopIdParser.Parse(raw);
        if (ids.Count == 0)
        {
            return [];
        }

        var result = new DesktopInfo[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            result[i] = new DesktopInfo(ids[i], i, TryReadName(key, ids[i]));
        }

        return result;
    }
#pragma warning restore CA1822

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _changeEvent.Set();
        _watchThread.Join(TimeSpan.FromSeconds(2));
        _changeEvent.Dispose();

        // Blocks until any RaiseChanged() call currently past its disposed-check finishes, so
        // Changed can never be raised after this method returns.
        lock (_raiseGate)
        {
            _debouncer.Dispose();
        }
    }

    private static string? TryReadName(RegistryKey virtualDesktopsKey, Guid desktopId)
    {
        try
        {
            using var nameKey = virtualDesktopsKey.OpenSubKey($@"Desktops\{desktopId:B}");
            var name = nameKey?.GetValue("Name") as string;
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private void WatchLoop()
    {
        RegistryKey? key = null;
        try
        {
            while (!_disposed)
            {
                try
                {
                    key ??= Registry.CurrentUser.OpenSubKey(KeyPath);
                    if (key is null)
                    {
                        Log?.Invoke("RegistryWatch: key missing");
                        // The key is created by Explorer on first use of virtual desktops (a fresh
                        // profile, or one that has never opened Task View, has none); we can't
                        // register a native watch against a key that doesn't exist, so fall back to a
                        // slow poll until it appears. ReadDesktops() already treats "no key" as zero
                        // desktops (the implicit single desktop), so callers see correct behaviour
                        // throughout, just without live updates until the key is created.
                        if (WaitOrExit(TimeSpan.FromSeconds(2)))
                        {
                            return;
                        }

                        continue;
                    }

                    if (!TryArm(key))
                    {
                        key.Dispose();
                        key = null;
                        if (WaitOrExit(TimeSpan.FromSeconds(2)))
                        {
                            return;
                        }

                        continue;
                    }

                    _changeEvent.WaitOne();
                    if (_disposed)
                    {
                        return;
                    }

                    // Re-arm the watch BEFORE notifying subscribers (and before the debounce delay
                    // elapses), so a change that lands while a subscriber is busy handling the
                    // previous one - or during the debounce window itself - is never missed. Only the
                    // single RegNotifyChangeKeyValue call below is one-shot; the key handle itself can
                    // be reused across arms.
                    if (!TryArm(key))
                    {
                        key.Dispose();
                        key = null;
                    }

                    _debouncer.Signal();
                }
                catch (Exception ex)
                {
                    // Defense in depth: nothing in this loop is expected to throw (registry access
                    // is already guarded call-by-call), but if it ever does, the watch thread must
                    // not die silently - that would permanently stop live Dock-band/desktop-list
                    // updates for the rest of the process' life with no indication why. Log it,
                    // drop the (possibly now-invalid) key handle so the next iteration reopens it,
                    // back off via the existing WaitOrExit, and keep watching.
                    Log?.Invoke($"RegistryWatch: WatchLoop iteration failed: {ex}");
                    key?.Dispose();
                    key = null;
                    if (WaitOrExit(TimeSpan.FromSeconds(2)))
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            key?.Dispose();
        }
    }

    private bool TryArm(RegistryKey key)
    {
        var rc = global::DesktopTiler.Core.VirtualDesktops.Advapi32.RegNotifyChangeKeyValue(
            key.Handle,
            watchSubtree: true,
            dwNotifyFilter: NotifyFilter,
            hEvent: _changeEvent.SafeWaitHandle,
            fAsynchronous: true);
        if (rc != 0)
        {
            Log?.Invoke($"RegistryWatch: RegNotifyChangeKeyValue failed rc={rc}");
        }

        return rc == 0;
    }

    private void RaiseChanged()
    {
        lock (_raiseGate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                // A misbehaving subscriber must not kill the watch loop or the debounce timer thread.
            }
        }
    }

    /// <summary>Waits for <paramref name="delay"/> - used only when the VirtualDesktops key
    /// doesn't exist yet or arming the watch failed, i.e. no real notification can signal
    /// <see cref="_changeEvent"/> in the meantime - or returns early (true) if Dispose() signals
    /// it, so shutdown isn't delayed by up to <paramref name="delay"/>. Waits on the event itself
    /// rather than sleeping in small increments, so there's no polling.</summary>
    private bool WaitOrExit(TimeSpan delay)
    {
        _changeEvent.WaitOne(delay);
        return _disposed;
    }
}
