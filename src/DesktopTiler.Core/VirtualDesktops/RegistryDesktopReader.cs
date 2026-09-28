using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>
/// Reads the ordered list of virtual desktops (and their optional names) from
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops</c>, and detects
/// changes to it by polling a cheap fingerprint of that key (id list, current desktop, names).
///
/// Why polling rather than <c>RegNotifyChangeKeyValue</c>: under MSIX registry virtualization the
/// key can still be read, but change notifications for Explorer's writes never reach a packaged
/// process. Making them arrive needs the restricted <c>unvirtualizedResources</c> capability,
/// which the Microsoft Store declined (policy 10.6.3). A handful of registry reads every
/// <see cref="PollInterval"/> is a far smaller cost than that capability.
/// </summary>
public sealed class RegistryDesktopReader : IDisposable
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private const string ValueName = "VirtualDesktopIDs";
    private const string CurrentValueName = "CurrentVirtualDesktop";

    /// <summary>Short enough that the Dock band's active-desktop marker follows a keyboard
    /// switch without a noticeable lag; long enough to be negligible CPU-wise.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly ManualResetEvent _stopEvent = new(false);
    private readonly Thread _pollThread;

    // Guards RaiseChanged() against Dispose(): if the poll thread is still inside RaiseChanged()
    // when Dispose()'s bounded Join gives up, taking this lock blocks until it finishes, so
    // Changed can never be raised after Dispose() has returned.
    private readonly object _raiseGate = new();
    private volatile bool _disposed;

    public RegistryDesktopReader()
    {
        _pollThread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name = "DesktopTiler-RegistryPoll",
        };
        _pollThread.Start();
    }

    /// <summary>Failure diagnostics hook, wired to SpikeLog by the app (Core can't reference it).</summary>
    public static Action<string>? Log { get; set; }

    /// <summary>Raised (on the background poll thread) whenever the desktop list, the current
    /// desktop, or a desktop name changes. Consumers should re-read via <see cref="ReadDesktops"/>.</summary>
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

    /// <summary>Folds everything the Dock band renders from the registry into one comparable
    /// string. Pure, so the change detection is unit-testable without a registry.</summary>
    public static string Fingerprint(byte[]? desktopIds, byte[]? currentDesktop, IEnumerable<string?> names)
    {
        // U+001F (unit separator) can't be typed into a Task View desktop name, so in practice two
        // different name lists never join to the same string (worst case: one missed refresh).
        return Convert.ToHexString(desktopIds ?? [])
            + "|" + Convert.ToHexString(currentDesktop ?? [])
            + "|" + string.Join('\u001f', names.Select(n => n ?? string.Empty));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopEvent.Set();
        if (_pollThread.Join(TimeSpan.FromSeconds(2)))
        {
            _stopEvent.Dispose();
            return;
        }

        // The poll thread is stuck in a slow subscriber (Changed runs on it). Wait for that call
        // to finish so Changed can't fire after we return, but leave _stopEvent undisposed: the
        // thread still has to reach its WaitOne, which would throw on a closed handle and take the
        // process down. Leaking one event handle at shutdown is harmless.
        lock (_raiseGate)
        {
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

    /// <summary>The current fingerprint, or null if the registry couldn't be read this time (the
    /// caller then keeps its previous one). A missing key - a fresh profile that has never used
    /// virtual desktops - is a valid, empty fingerprint, so Explorer creating it later is picked
    /// up like any other change.</summary>
    private static string? TryReadFingerprint()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key is null)
            {
                return string.Empty;
            }

            var raw = key.GetValue(ValueName) as byte[];
            var current = key.GetValue(CurrentValueName) as byte[];
            var names = VirtualDesktopIdParser.Parse(raw).Select(id => TryReadName(key, id));
            return Fingerprint(raw, current, names);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"RegistryPoll: read failed: {ex.Message}");
            return null;
        }
    }

    private void PollLoop()
    {
        var last = TryReadFingerprint();

        // WaitOne returns true only once Dispose() signals the stop event.
        while (!_stopEvent.WaitOne(PollInterval))
        {
            var now = TryReadFingerprint();
            if (now is null)
            {
                continue;
            }

            // A null `last` (the startup read failed) also raises once: the page may have rendered
            // from a registry state we never fingerprinted.
            if (!string.Equals(now, last, StringComparison.Ordinal))
            {
                RaiseChanged();
            }

            last = now;
        }
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
                // A misbehaving subscriber must not kill the poll loop.
            }
        }
    }
}
