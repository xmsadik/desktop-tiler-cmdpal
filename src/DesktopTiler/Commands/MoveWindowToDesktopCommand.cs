using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using DesktopTiler.Core.VirtualDesktops;
using DesktopTiler.Core.Windows;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// "Move window to desktop 1".."9" (stable ids "DesktopTiler.move.1".."9") and "Move window to
/// next/previous desktop" ("DesktopTiler.move.next"/"prev"): sends the active app window to that
/// desktop and stays on the current one. The window is the foreground one when invoked by hotkey,
/// otherwise the top-most app window on this desktop (see
/// <see cref="TargetWindowSelector.SelectActiveAppWindow"/>). Next/previous follow registry display
/// order and stop at the ends, like <see cref="StepDesktopCommand"/>.
/// </summary>
internal sealed partial class MoveWindowToDesktopCommand : InvokableCommand
{
    private const int EAccessDenied = unchecked((int)0x80070005);

    private readonly VdComClient _vdClient;
    private readonly RegistryDesktopReader _registryReader;
    private readonly int _number; // 1-based desktop number, or 0 for a step command
    private readonly int _step;

    private MoveWindowToDesktopCommand(VdComClient vdClient, RegistryDesktopReader registryReader, int number, int step)
    {
        _vdClient = vdClient;
        _registryReader = registryReader;
        _number = number;
        _step = step;
        Icon = new IconInfo(""); // MoveToFolder
    }

    public static MoveWindowToDesktopCommand ToNumber(VdComClient vdClient, RegistryDesktopReader registryReader, int number)
    {
        var n = number.ToString(CultureInfo.InvariantCulture);
        return new(vdClient, registryReader, number, 0)
        {
            Id = $"DesktopTiler.move.{n}",
            Name = $"Move window to desktop {n}",
        };
    }

    public static MoveWindowToDesktopCommand ByStep(VdComClient vdClient, RegistryDesktopReader registryReader, int step) =>
        new(vdClient, registryReader, 0, step)
        {
            Id = step > 0 ? "DesktopTiler.move.next" : "DesktopTiler.move.prev",
            Name = step > 0 ? "Move window to next desktop" : "Move window to previous desktop",
        };

    public override CommandResult Invoke()
    {
        using var timing = SpikeLog.Timed(Id);
        try
        {
            var desktops = _registryReader.ReadDesktops();
            if (desktops.Count == 0)
            {
                desktops = DesktopListReader.FallbackToVdComClient(_vdClient);
            }

            var currentId = _vdClient.GetCurrentIdAsync().WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
            int targetIndex;
            if (_number > 0)
            {
                targetIndex = _number - 1;
                if (targetIndex >= desktops.Count)
                {
                    return CommandResult.ShowToast($"Desktop {_number.ToString(CultureInfo.InvariantCulture)} doesn't exist");
                }
            }
            else
            {
                var currentIndex = Math.Max(0, desktops.ToList().FindIndex(d => d.Id == currentId));
                targetIndex = currentIndex + _step;
                if (targetIndex < 0 || targetIndex >= desktops.Count)
                {
                    return CommandResult.ShowToast(_step > 0 ? "Already on the last desktop" : "Already on the first desktop");
                }
            }

            var target = desktops[targetIndex];
            if (target.Id == currentId)
            {
                return CommandResult.ShowToast("The window is already on that desktop");
            }

            var window = SelectWindow();
            if (window is null)
            {
                return CommandResult.ShowToast("No window to move on this desktop");
            }

            _vdClient.MoveWindowAsync(window.Value, target.Id).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
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
            return DesktopCommandErrors.ToToastResult(Id, ex);
        }
    }

    private nint? SelectWindow()
    {
        var ownProcessId = Environment.ProcessId;
        bool IsOnCurrentDesktop(nint hwnd) => _vdClient.IsWindowOnCurrentVirtualDesktopAsync(hwnd).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);

        var foreground = WindowEnumerator.DescribeForeground(IsOnCurrentDesktop);

        // Cheap filters first, then the per-window desktop check (a COM call) only for what's left.
        var candidates = WindowEnumerator.EnumerateZOrder(null)
            .Where(w => !w.IsCloaked && DesktopOccupancy.IsAppWindow(w, ownProcessId, WindowEnumerator.CmdPalImageName))
            .Select(w => w with { IsOnCurrentVirtualDesktop = IsOnCurrentDesktop(w.Handle) })
            .ToArray();

        return TargetWindowSelector.SelectActiveAppWindow(foreground, candidates, ownProcessId, WindowEnumerator.CmdPalImageName)?.Handle;
    }
}
