using System;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>Switches to a specific desktop id (used by the Dock band's per-desktop items).
/// Notifies the owning <see cref="DesktopsPage"/> afterwards so the band re-renders which
/// desktop is active, since the COM "current desktop" can flip before the registry key does.</summary>
internal sealed partial class SwitchDesktopCommand : InvokableCommand
{
    private readonly VdComClient _vdClient;
    private readonly Guid _desktopId;
    private readonly DesktopsPage _owner;

    public SwitchDesktopCommand(VdComClient vdClient, Guid desktopId, DesktopsPage owner)
    {
        _vdClient = vdClient;
        _desktopId = desktopId;
        _owner = owner;
        Icon = new IconInfo("");
    }

    public override CommandResult Invoke()
    {
        var commandId = $"DesktopTiler.dock.switch({_desktopId})";
        using var timing = SpikeLog.Timed(commandId);
        try
        {
            _vdClient.SwitchAsync(_desktopId, animate: true).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
            _owner.NotifyAfterSwitch();
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
            return DesktopCommandErrors.ToToastResult(commandId, ex);
        }
    }
}
