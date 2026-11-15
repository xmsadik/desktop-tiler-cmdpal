using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace DesktopTiler;

/// <summary>
/// Ends this extension process when the Command Palette host that started it exits.
///
/// Why: if the host goes away without disposing the extension (it was restarted, updated or
/// crashed), this COM server stays alive, and the next host connects to the same objects. Those
/// still carry the dead host's event subscriptions (ItemsChanged / PropertyChanged); raising an
/// event then fails on the dead subscriber first and never reaches the new host, so the Dock band
/// silently stops updating (active ring, occupancy icons). Exiting with the host lets the next
/// one start a fresh process with clean subscriptions. Process-exit notification is immediate,
/// while a restarted host takes seconds to come back and load extensions, so the old process is
/// gone before the new host looks for it.
/// </summary>
internal static partial class HostProcessWatch
{
    private const string HostProcessName = "Microsoft.CmdPal.UI";

    /// <summary>A wait handle that becomes signalled when the running Command Palette host exits,
    /// or null if no host process is found (then nothing is watched and the process lives until
    /// the host disposes the extension, as before).</summary>
    public static WaitHandle? TryGetHostExitedHandle()
    {
        try
        {
            var hosts = Process.GetProcessesByName(HostProcessName);
            if (hosts.Length == 0)
            {
                SpikeLog.WriteLine("HostProcessWatch: no Command Palette host found, not watching");
                return null;
            }

            // Command Palette is single-instance; if there are several, watch the newest one -
            // the one that just activated us.
            var host = hosts[0];
            foreach (var candidate in hosts)
            {
                if (candidate.StartTime > host.StartTime)
                {
                    host = candidate;
                }
            }

            foreach (var other in hosts)
            {
                if (!ReferenceEquals(other, host))
                {
                    other.Dispose();
                }
            }

            SpikeLog.WriteLine($"HostProcessWatch: watching host pid {host.Id}");

            // The Process object owns the native handle; the wait handle borrows it
            // (ownsHandle: false) and keeps the Process alive, disposing it with itself.
            return new HostExitedHandle(host);
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"HostProcessWatch: could not open the host process: {ex.Message}");
            return null;
        }
    }

    private sealed partial class HostExitedHandle : WaitHandle
    {
        private readonly Process _host;

        public HostExitedHandle(Process host)
        {
            _host = host;
            SafeWaitHandle = new SafeWaitHandle(host.Handle, ownsHandle: false);
        }

        protected override void Dispose(bool explicitDisposing)
        {
            base.Dispose(explicitDisposing);
            if (explicitDisposing)
            {
                _host.Dispose();
            }
        }
    }
}
