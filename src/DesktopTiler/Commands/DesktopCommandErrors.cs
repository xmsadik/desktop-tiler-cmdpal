using System;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// Shared "something unexpected happened switching desktops" handling for the desktop commands
/// (<see cref="DesktopNumberCommand"/>, <see cref="StepDesktopCommand"/>,
/// <see cref="SwitchDesktopCommand"/>). Each command's <c>Invoke()</c> already special-cases
/// <see cref="DesktopTiler.Core.VirtualDesktops.UnsupportedBuildException"/> with its own toast;
/// everything else - a COMException surviving VdComClient's one retry, an
/// InvalidOperationException when the registry and COM desktop lists briefly disagree mid-change,
/// a SecurityException reading the registry, etc. - must never escape <c>Invoke()</c> (that would
/// crash the host), so it's logged to SpikeLog and shown as a toast instead.
/// </summary>
internal static class DesktopCommandErrors
{
    public static CommandResult ToToastResult(string commandId, Exception ex)
    {
        SpikeLog.WriteLine($"{commandId} failed: {ex}");
        return CommandResult.ShowToast($"Desktop switch failed: {ShortMessage(ex)}");
    }

    private static string ShortMessage(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
}
