using System;
using System.Linq;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>"Last used desktop" (stable id "DesktopTiler.desktop.last"): switches to the desktop
/// that was active before the current one, so pressing it repeatedly flips between the two most
/// recent desktops. The history is fed by the registry poll (see <see cref="DesktopHistory"/>),
/// so switches made any way - Windows shortcuts, Task View, this extension - count.</summary>
internal sealed partial class LastDesktopCommand : InvokableCommand
{
    private readonly VdComClient _vdClient;
    private readonly RegistryDesktopReader _registryReader;
    private readonly DesktopHistory _history;

    public LastDesktopCommand(VdComClient vdClient, RegistryDesktopReader registryReader, DesktopHistory history)
    {
        _vdClient = vdClient;
        _registryReader = registryReader;
        _history = history;

        Id = "DesktopTiler.desktop.last";
        Name = "Last used desktop";
        Icon = new IconInfo(""); // Switch
    }

    public override CommandResult Invoke()
    {
        using var timing = SpikeLog.Timed(Id);
        try
        {
            if (_history.Previous is not { } previous)
            {
                return CommandResult.ShowToast("No previous desktop yet");
            }

            var desktops = _registryReader.ReadDesktops();
            if (desktops.Count == 0)
            {
                desktops = DesktopListReader.FallbackToVdComClient(_vdClient);
            }

            if (desktops.All(d => d.Id != previous))
            {
                return CommandResult.ShowToast("The last used desktop no longer exists");
            }

            _vdClient.SwitchAsync(previous, animate: true).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
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
