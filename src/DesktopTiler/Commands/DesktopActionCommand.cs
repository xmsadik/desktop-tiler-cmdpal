using System;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>A desktop management action (new, delete, move left/right), as a top-level command
/// or a context menu entry. Wraps the action with the same error handling as the other desktop
/// commands, since an exception escaping Invoke() would crash the host.</summary>
internal sealed partial class DesktopActionCommand : InvokableCommand
{
    private readonly Func<CommandResult> _action;

    /// <param name="id">Stable id for a top-level command (so a hotkey can be bound to it);
    /// null for a context menu entry.</param>
    public DesktopActionCommand(string? id, string name, IconInfo icon, Func<CommandResult> action)
    {
        if (id is not null)
        {
            Id = id;
        }

        Name = name;
        Icon = icon;
        _action = action;
    }

    public override CommandResult Invoke()
    {
        var commandId = string.IsNullOrEmpty(Id) ? $"DesktopTiler.desktop.action: {Name}" : Id;
        using var timing = SpikeLog.Timed(commandId);
        try
        {
            return _action();
        }
        catch (Exception ex)
        {
            return DesktopManager.ToToast(commandId, ex);
        }
    }
}
