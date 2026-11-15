using System;
using System.Runtime.InteropServices;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>One entry of an "All windows" row's context menu (bring here, move to desktop N,
/// close). Wraps the action with the same error handling as the other desktop commands, since an
/// exception escaping Invoke() would crash the host.</summary>
internal sealed partial class WindowActionCommand : InvokableCommand
{
    private const int EAccessDenied = unchecked((int)0x80070005);

    private readonly Func<CommandResult> _action;

    public WindowActionCommand(string name, IconInfo icon, Func<CommandResult> action)
    {
        Name = name;
        Icon = icon;
        _action = action;
    }

    public override CommandResult Invoke()
    {
        const string commandId = "DesktopTiler.windows.action";
        using var timing = SpikeLog.Timed($"{commandId}: {Name}");
        try
        {
            return _action();
        }
        catch (UnsupportedBuildException)
        {
            return CommandResult.ShowToast("Unsupported Windows build for desktop switching");
        }
        catch (ExplorerNotRespondingException)
        {
            return CommandResult.ShowToast("Explorer is not responding");
        }
        catch (NotSupportedException ex)
        {
            return CommandResult.ShowToast(ex.Message);
        }
        catch (COMException ex) when (ex.HResult == EAccessDenied)
        {
            return CommandResult.ShowToast("This window can't be moved (it may be running as administrator)");
        }
        catch (Exception ex)
        {
            return DesktopCommandErrors.ToToastResult(commandId, ex);
        }
    }
}
