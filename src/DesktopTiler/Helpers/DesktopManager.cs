using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// Desktop management - new, delete, rename, move left/right - shared by the top-level commands
/// and the "Manage desktops" page. (Not on the Dock buttons: the Command Palette 0.102 Dock shows
/// only its own right-click menu, not an item's MoreCommands.) Every operation takes a desktop
/// id, or null for "the current desktop" (resolved when the command runs, so a top-level command
/// always acts on whichever desktop is current then). The Dock band and the pages pick up the
/// result by themselves through the registry poll (<see cref="RegistryDesktopReader.Changed"/>).
///
/// <c>keepOpen</c> on the operations: true when invoked from the "Manage desktops"
/// page, which stays open so several changes can be made in a row; false elsewhere (the palette
/// closes, any message shows as a toast).
/// </summary>
internal sealed class DesktopManager
{
    private static readonly IconInfo NewIcon = new(""); // Add
    private static readonly IconInfo DeleteIcon = new(""); // Delete
    private static readonly IconInfo LeftIcon = new(""); // Back
    private static readonly IconInfo RightIcon = new(""); // Forward

    private readonly VdComClient _vdClient;
    private readonly RegistryDesktopReader _registryReader;

    public DesktopManager(VdComClient vdClient, RegistryDesktopReader registryReader)
    {
        _vdClient = vdClient;
        _registryReader = registryReader;
    }

    /// <summary>"Desktop 2", or "Desktop 2 · Mail" when it has a name.</summary>
    public static string Label(DesktopInfo desktop)
    {
        var number = (desktop.Index + 1).ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(desktop.Name) ? $"Desktop {number}" : $"Desktop {number} · {desktop.Name}";
    }

    /// <summary>The toast for an operation that failed; never throws.</summary>
    public static CommandResult ToToast(string commandId, Exception ex) => ex switch
    {
        UnsupportedBuildException => CommandResult.ShowToast("Unsupported Windows build for desktop management"),
        ExplorerNotRespondingException => CommandResult.ShowToast("Explorer is not responding"),
        NotSupportedException or InvalidOperationException => CommandResult.ShowToast(ex.Message),
        _ => LogAndToast(commandId, ex),
    };

    /// <summary>The desktops in display order (registry, or the COM list on a profile that has
    /// never opened Task View).</summary>
    public IReadOnlyList<DesktopInfo> ReadDesktops()
    {
        var desktops = _registryReader.ReadDesktops();
        return desktops.Count > 0 ? desktops : DesktopListReader.FallbackToVdComClient(_vdClient);
    }

    public Guid CurrentId() => _vdClient.GetCurrentIdAsync().WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);

    /// <summary>The desktop <paramref name="id"/> (null: the current one) as it is listed now,
    /// with the full list; throws <see cref="InvalidOperationException"/> if it's gone.</summary>
    public (DesktopInfo Desktop, IReadOnlyList<DesktopInfo> All) Find(Guid? id)
    {
        var target = id ?? CurrentId();
        var desktops = ReadDesktops();
        foreach (var d in desktops)
        {
            if (d.Id == target)
            {
                return (d, desktops);
            }
        }

        throw new InvalidOperationException("That desktop no longer exists");
    }

    public CommandResult Create(bool follow, bool keepOpen)
    {
        var id = _vdClient.CreateDesktopAsync().WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
        if (follow)
        {
            _vdClient.SwitchAsync(id, animate: true).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
            return CommandResult.Dismiss();
        }

        // The registry may not list the new desktop yet; the COM list already does.
        var ids = _vdClient.GetDesktopIdsAsync().WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
        var number = (ids.ToList().IndexOf(id) + 1).ToString(CultureInfo.InvariantCulture);
        return Toast($"Desktop {number} created", keepOpen);
    }

    /// <summary>Asks for confirmation (the palette's own dialog), then deletes the desktop; its
    /// windows move to the neighbour named in the question.</summary>
    public CommandResult ConfirmDelete(Guid? id, bool keepOpen)
    {
        var (desktop, all) = Find(id);
        var fallback = DesktopManagementRules.FallbackIndex(all.Count, desktop.Index);
        if (fallback is not { } f)
        {
            return Toast("The last desktop can't be deleted", keepOpen);
        }

        var target = desktop.Id;
        var delete = new DesktopActionCommand(null, "Delete", DeleteIcon, () =>
        {
            _vdClient.RemoveDesktopAsync(target).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
            return keepOpen ? CommandResult.KeepOpen() : CommandResult.Dismiss();
        });

        return CommandResult.Confirm(new ConfirmationArgs
        {
            Title = $"Delete {Label(desktop)}?",
            Description = $"Its windows move to {Label(all[f])}.",
            PrimaryCommand = delete,
            IsPrimaryCommandCritical = true,
        });
    }

    public CommandResult Move(Guid? id, int step, bool keepOpen)
    {
        var (desktop, all) = Find(id);
        if (DesktopManagementRules.MoveTargetIndex(all.Count, desktop.Index, step) is not { } index)
        {
            return Toast(step < 0 ? $"{Label(desktop)} is already the first" : $"{Label(desktop)} is already the last", keepOpen);
        }

        _vdClient.MoveDesktopAsync(desktop.Id, index).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
        return keepOpen ? CommandResult.KeepOpen() : CommandResult.Dismiss();
    }

    public void Rename(Guid id, string? name) =>
        _vdClient.RenameDesktopAsync(id, DesktopManagementRules.NormalizeName(name)).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);

    public DesktopActionCommand NewCommand(bool follow, bool keepOpen, string? commandId = null) =>
        new(commandId, follow ? "New desktop and go" : "New desktop", NewIcon, () => Create(follow, keepOpen));

    public DesktopActionCommand DeleteCommand(Guid? id, bool keepOpen, string? commandId = null, string name = "Delete desktop") =>
        new(commandId, name, DeleteIcon, () => ConfirmDelete(id, keepOpen));

    public DesktopActionCommand MoveCommand(Guid? id, int step, bool keepOpen, string? commandId = null, string? name = null) =>
        new(commandId, name ?? (step < 0 ? "Move desktop left" : "Move desktop right"), step < 0 ? LeftIcon : RightIcon, () => Move(id, step, keepOpen));

    /// <summary>The context menu of one "Manage desktops" row.</summary>
    public IContextItem[] ContextItems(Guid id, bool keepOpen) =>
    [
        new CommandContextItem(new RenameDesktopPage(this, id, keepOpen)),
        new CommandContextItem(MoveCommand(id, -1, keepOpen)),
        new CommandContextItem(MoveCommand(id, +1, keepOpen)),
        new CommandContextItem(NewCommand(follow: false, keepOpen)),
        new CommandContextItem(DeleteCommand(id, keepOpen)) { IsCritical = true },
    ];

    private static CommandResult Toast(string message, bool keepOpen) => CommandResult.ShowToast(new ToastArgs
    {
        Message = message,
        Result = keepOpen ? CommandResult.KeepOpen() : CommandResult.Dismiss(),
    });

    private static CommandResult LogAndToast(string commandId, Exception ex)
    {
        SpikeLog.WriteLine($"{commandId} failed: {ex}");
        var message = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
        return CommandResult.ShowToast(ex is COMException
            ? $"Desktop change failed (0x{ex.HResult:X8})"
            : $"Desktop change failed: {message}");
    }
}
