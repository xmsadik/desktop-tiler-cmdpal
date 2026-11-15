using System;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>An "All windows" row: switches to the window's desktop (if it isn't the current one)
/// and brings the window to the front. Notifies the Dock band afterwards so its active-desktop
/// marker updates straight away.</summary>
internal sealed partial class ActivateWindowCommand : InvokableCommand
{
    private readonly VdComClient _vdClient;
    private readonly Guid? _desktopId;
    private readonly nint _window;
    private readonly DesktopsPage _desktopsPage;

    public ActivateWindowCommand(VdComClient vdClient, Guid? desktopId, nint window, DesktopsPage desktopsPage)
    {
        _vdClient = vdClient;
        _desktopId = desktopId;
        _window = window;
        _desktopsPage = desktopsPage;
        Name = "Switch to window";
    }

    public override CommandResult Invoke()
    {
        const string commandId = "DesktopTiler.windows.activate";
        using var timing = SpikeLog.Timed(commandId);
        try
        {
            _vdClient.ActivateWindowAsync(_desktopId, _window, animate: true).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
            _desktopsPage.NotifyAfterSwitch();
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
