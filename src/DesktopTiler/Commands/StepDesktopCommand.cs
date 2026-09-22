using System;
using System.Linq;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>"Next desktop" / "Previous desktop" - stable ids "DesktopTiler.desktop.next" and
/// "DesktopTiler.desktop.prev". Stepping is by registry display order (not the undocumented
/// GetAdjacentDesktop COM call), clamped at the ends rather than wrapping.</summary>
internal sealed partial class StepDesktopCommand : InvokableCommand
{
    private readonly VdComClient _vdClient;
    private readonly RegistryDesktopReader _registryReader;
    private readonly int _step;

    public StepDesktopCommand(VdComClient vdClient, RegistryDesktopReader registryReader, int step)
    {
        _vdClient = vdClient;
        _registryReader = registryReader;
        _step = step;

        Id = step > 0 ? "DesktopTiler.desktop.next" : "DesktopTiler.desktop.prev";
        Name = step > 0 ? "Next desktop" : "Previous desktop";
        Icon = new IconInfo(step > 0 ? "" : "");
    }

    public override CommandResult Invoke()
    {
        using var timing = SpikeLog.Timed(Id);

        // The whole body is guarded, not just the switch call: ReadDesktops() (registry access)
        // can throw too, and an exception escaping Invoke() would crash the host rather than just
        // this command.
        try
        {
            var desktops = _registryReader.ReadDesktops();
            if (desktops.Count == 0)
            {
                // A fresh profile that's never opened Task View has no VirtualDesktops registry
                // key at all - fall back to the COM enumeration for the single desktop it still
                // has (see DesktopListReader) before declaring it missing.
                desktops = DesktopListReader.FallbackToVdComClient(_vdClient);
                if (desktops.Count == 0)
                {
                    return CommandResult.ShowToast("No virtual desktops found");
                }
            }

            var currentId = _vdClient.GetCurrentIdAsync().WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
            var currentIndex = desktops.ToList().FindIndex(d => d.Id == currentId);
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            var targetIndex = currentIndex + _step;
            if (targetIndex < 0 || targetIndex >= desktops.Count)
            {
                return CommandResult.ShowToast(_step > 0 ? "Already on the last desktop" : "Already on the first desktop");
            }

            _vdClient.SwitchAsync(desktops[targetIndex].Id, animate: true).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
            return CommandResult.Dismiss();
        }
        catch (UnsupportedBuildException)
        {
            return CommandResult.ShowToast("Unsupported Windows build for desktop switching");
        }
        catch (ExplorerNotRespondingException)
        {
            return CommandResult.ShowToast("Explorer is not responding");
        }
        catch (Exception ex)
        {
            return DesktopCommandErrors.ToToastResult(Id, ex);
        }
    }
}
