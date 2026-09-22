using System;
using System.Globalization;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// One of the stable "DesktopTiler.desktop.1".."DesktopTiler.desktop.9" top-level commands.
/// The id is stable across desktop add/remove so a hotkey bound to "go to desktop 3" keeps
/// working even if desktops are added or removed later - it always means "the 3rd desktop in
/// display order right now", not a specific desktop id.
/// </summary>
internal sealed partial class DesktopNumberCommand : InvokableCommand
{
    private readonly VdComClient _vdClient;
    private readonly RegistryDesktopReader _registryReader;
    private readonly int _number;

    public DesktopNumberCommand(VdComClient vdClient, RegistryDesktopReader registryReader, int number)
    {
        _vdClient = vdClient;
        _registryReader = registryReader;
        _number = number;

        Id = $"DesktopTiler.desktop.{number.ToString(CultureInfo.InvariantCulture)}";
        Name = $"Desktop {number.ToString(CultureInfo.InvariantCulture)}";
        Icon = new IconInfo("");
    }

    public override CommandResult Invoke()
    {
        using var timing = SpikeLog.Timed(Id);

        // The whole body is guarded, not just the switch call: ReadDesktops() (registry access)
        // can throw too (e.g. a SecurityException), and an exception escaping Invoke() would
        // crash the host rather than just this command.
        try
        {
            var desktops = _registryReader.ReadDesktops();
            if (desktops.Count == 0)
            {
                // A fresh profile that's never opened Task View has no VirtualDesktops registry
                // key at all - fall back to the COM enumeration for the single desktop it still
                // has (see DesktopListReader) before declaring it missing.
                desktops = DesktopListReader.FallbackToVdComClient(_vdClient);
            }

            if (_number < 1 || _number > desktops.Count)
            {
                return CommandResult.ShowToast($"Desktop {_number.ToString(CultureInfo.InvariantCulture)} doesn't exist");
            }

            _vdClient.SwitchAsync(desktops[_number - 1].Id, animate: true).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
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
