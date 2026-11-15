using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DesktopTiler.Core.Rules;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// "Desktop workspaces": a "Save layout as desktop workspace" row, then one row per workspace. Choosing a
/// workspace opens it; its context menu edits its apps, renames it, or deletes it.
/// </summary>
internal sealed partial class WorkspacesPage : ListPage, IDisposable
{
    internal static readonly IconInfo WorkspaceIcon = new(""); // overlapping windows
    internal static readonly IconInfo SaveIcon = new(""); // Save
    private static readonly IconInfo EditIcon = new(""); // Edit
    private static readonly IconInfo DeleteIcon = new(""); // Delete

    private readonly WorkspaceStore _store;
    private readonly WorkspaceLauncher _launcher;
    private readonly DesktopManager _desktops;

    // One edit page per workspace, reused across refreshes: each listens to the store, so creating
    // a new one on every refresh would pile up subscriptions. Touched on host threads; locked.
    private readonly Dictionary<Guid, WorkspaceEditPage> _editPages = [];

    public WorkspacesPage(WorkspaceStore store, WorkspaceLauncher launcher, DesktopManager desktops)
    {
        _store = store;
        _launcher = launcher;
        _desktops = desktops;

        Id = "DesktopTiler.workspaces";
        Name = "Desktop workspaces";
        Title = "Desktop workspaces";
        Icon = WorkspaceIcon;
        PlaceholderText = "Search desktop workspaces; choose one to open it";

        _store.Changed += OnStoreChanged;
    }

    public void Dispose()
    {
        _store.Changed -= OnStoreChanged;
        lock (_editPages)
        {
            foreach (var page in _editPages.Values)
            {
                page.Dispose();
            }

            _editPages.Clear();
        }
    }

    /// <summary>The command that opens <paramref name="workspace"/>; also the per-workspace
    /// top-level command, with a stable id so a hotkey can be bound to it.</summary>
    public static DesktopActionCommand OpenCommand(WorkspaceLauncher launcher, Workspace workspace, bool keepOpen, bool topLevel = false)
    {
        var id = workspace.Id;
        return new DesktopActionCommand(
            topLevel ? $"DesktopTiler.workspace.open.{id:N}" : null,
            topLevel ? $"Open desktop workspace: {workspace.Name}" : "Open desktop workspace",
            WorkspaceIcon,
            () => launcher.Open(id, keepOpen));
    }

    /// <summary>"3 apps · desktops 1, 3".</summary>
    internal static string Describe(Workspace workspace, IReadOnlyList<DesktopInfo> desktops)
    {
        var apps = workspace.Entries.Count == 1 ? "1 app" : $"{workspace.Entries.Count} apps";
        var numbers = workspace.Entries
            .Select(e => desktops.FirstOrDefault(d => d.Id == e.DesktopId))
            .Where(d => d.Id != Guid.Empty)
            .Select(d => d.Index + 1)
            .Distinct()
            .Order()
            .Select(n => n.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        return numbers.Length switch
        {
            0 => apps,
            1 => $"{apps} · desktop {numbers[0]}",
            _ => $"{apps} · desktops {string.Join(", ", numbers)}",
        };
    }

    public override IListItem[] GetItems()
    {
        // Called directly by the host: must never throw.
        try
        {
            var desktops = _desktops.ReadDesktops();
            var items = new List<IListItem>
            {
                new ListItem(new DesktopActionCommand(null, "Save layout as desktop workspace", SaveIcon, () => _launcher.SaveCurrentLayout(keepOpen: true)))
                {
                    Subtitle = "Every open app with the desktop it's on",
                },
            };

            foreach (var ws in _store.Workspaces)
            {
                var id = ws.Id;
                items.Add(new ListItem(OpenCommand(_launcher, ws, keepOpen: false))
                {
                    Title = ws.Name,
                    Subtitle = Describe(ws, desktops),
                    Icon = WorkspaceIcon,
                    MoreCommands =
                    [
                        new CommandContextItem(EditPage(id)),
                        new CommandContextItem(new RenameWorkspacePage(_store, id)),
                        new CommandContextItem(new DesktopActionCommand(null, "Delete desktop workspace", DeleteIcon, () => ConfirmDelete(id))) { IsCritical = true },
                    ],
                });
            }

            ForgetEditPages(_store.Workspaces.Select(w => w.Id));
            return [.. items];
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"WorkspacesPage.GetItems failed: {ex}");
            return [new ListItem { Title = "Workspaces unavailable" }];
        }
    }

    private WorkspaceEditPage EditPage(Guid id)
    {
        lock (_editPages)
        {
            if (!_editPages.TryGetValue(id, out var page))
            {
                page = new WorkspaceEditPage(_store, _desktops, id) { Icon = EditIcon };
                _editPages[id] = page;
            }

            return page;
        }
    }

    private void ForgetEditPages(IEnumerable<Guid> keep)
    {
        var alive = keep.ToHashSet();
        lock (_editPages)
        {
            foreach (var id in _editPages.Keys.Where(k => !alive.Contains(k)).ToArray())
            {
                _editPages[id].Dispose();
                _editPages.Remove(id);
            }
        }
    }

    private CommandResult ConfirmDelete(Guid id)
    {
        if (_store.Find(id) is not { } ws)
        {
            return CommandResult.KeepOpen();
        }

        return CommandResult.Confirm(new ConfirmationArgs
        {
            Title = $"Delete desktop workspace \"{ws.Name}\"?",
            Description = "Only the workspace is deleted; no app is closed.",
            PrimaryCommand = new DesktopActionCommand(null, "Delete", DeleteIcon, () =>
            {
                _store.Remove(id);
                return CommandResult.KeepOpen();
            }),
            IsPrimaryCommandCritical = true,
        });
    }

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        try
        {
            RaiseItemsChanged();
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"WorkspacesPage refresh failed: {ex.Message}");
        }
    }
}

/// <summary>"Edit apps": one row per app of a workspace with the desktop it goes to. Choosing a row
/// picks another desktop; its context menu removes the app from the workspace.</summary>
internal sealed partial class WorkspaceEditPage : ListPage, IDisposable
{
    private static readonly IconInfo RemoveIcon = new(""); // Remove
    private static readonly IconInfo AppFallbackIcon = new(""); // Window

    private readonly WorkspaceStore _store;
    private readonly DesktopManager _desktops;
    private readonly Guid _workspaceId;

    public WorkspaceEditPage(WorkspaceStore store, DesktopManager desktops, Guid workspaceId)
    {
        _store = store;
        _desktops = desktops;
        _workspaceId = workspaceId;

        Name = "Edit apps";
        Title = "Edit workspace apps";
        PlaceholderText = "Choose an app to change its desktop; remove it from its menu";

        _store.Changed += OnStoreChanged;
    }

    public void Dispose() => _store.Changed -= OnStoreChanged;

    public override IListItem[] GetItems()
    {
        try
        {
            if (_store.Find(_workspaceId) is not { } ws)
            {
                return [new ListItem(new NoOpCommand()) { Title = "This workspace no longer exists" }];
            }

            if (ws.Entries.Count == 0)
            {
                return [new ListItem(new NoOpCommand()) { Title = "No apps left in this workspace" }];
            }

            var desktops = _desktops.ReadDesktops();
            return
            [
                .. ws.Entries.Select(e =>
                {
                    var app = e.App;
                    var where = desktops.FirstOrDefault(d => d.Id == e.DesktopId) is { Id: var found } d && found != Guid.Empty
                        ? DesktopManager.Label(d)
                        : "its desktop was deleted - choose another";
                    var remove = new DesktopActionCommand(null, "Remove from workspace", RemoveIcon, () =>
                    {
                        _store.RemoveEntry(_workspaceId, app);
                        return CommandResult.KeepOpen();
                    });
                    return new ListItem(new EntryDesktopPage(_store, _desktops, _workspaceId, e))
                    {
                        Title = Path.GetFileNameWithoutExtension(app),
                        Subtitle = $"Opens on {where} · {e.ImagePath}",
                        Icon = File.Exists(e.ImagePath) ? new IconInfo(e.ImagePath) : AppFallbackIcon,
                        MoreCommands = [new CommandContextItem(remove) { IsCritical = true }],
                    };
                }),
            ];
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"WorkspaceEditPage.GetItems failed: {ex}");
            return [new ListItem { Title = "Workspace unavailable" }];
        }
    }

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        try
        {
            RaiseItemsChanged();
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"WorkspaceEditPage refresh failed: {ex.Message}");
        }
    }
}

/// <summary>Picks the desktop one app of a workspace goes to.</summary>
internal sealed partial class EntryDesktopPage : ListPage
{
    private readonly WorkspaceStore _store;
    private readonly DesktopManager _desktops;
    private readonly Guid _workspaceId;
    private readonly WorkspaceEntry _entry;

    public EntryDesktopPage(WorkspaceStore store, DesktopManager desktops, Guid workspaceId, WorkspaceEntry entry)
    {
        _store = store;
        _desktops = desktops;
        _workspaceId = workspaceId;
        _entry = entry;

        Name = "Choose desktop";
        Title = $"Open {Path.GetFileNameWithoutExtension(entry.App)} on";
        Icon = WorkspacesPage.WorkspaceIcon;
        PlaceholderText = "Choose the desktop this app goes to";
    }

    public override IListItem[] GetItems()
    {
        try
        {
            return
            [
                .. _desktops.ReadDesktops().Select(d =>
                {
                    var target = d.Id;
                    return new ListItem(new DesktopActionCommand(null, "Choose", WorkspacesPage.WorkspaceIcon, () =>
                    {
                        _store.SetEntryDesktop(_workspaceId, _entry.App, target);
                        return CommandResult.GoBack();
                    }))
                    {
                        Title = DesktopManager.Label(d),
                        Subtitle = d.Id == _entry.DesktopId ? "Current choice" : string.Empty,
                    };
                }),
            ];
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"EntryDesktopPage.GetItems failed: {ex}");
            return [new ListItem { Title = "Desktop list unavailable" }];
        }
    }
}

/// <summary>"Rename workspace": a <see cref="NameForm"/> with the workspace's name.</summary>
internal sealed partial class RenameWorkspacePage : ContentPage
{
    private const int MaxNameLength = 100;

    private readonly WorkspaceStore _store;
    private readonly Guid _workspaceId;

    public RenameWorkspacePage(WorkspaceStore store, Guid workspaceId)
    {
        _store = store;
        _workspaceId = workspaceId;
        Name = "Rename desktop workspace";
        Title = "Rename desktop workspace";
        Icon = new IconInfo(""); // Rename
    }

    public override IContent[] GetContent()
    {
        if (_store.Find(_workspaceId) is not { } ws)
        {
            return [new MarkdownContent("This workspace no longer exists.")];
        }

        return
        [
            new NameForm("DesktopTiler.workspace.rename", $"Rename \"{ws.Name}\"", ws.Name, "Workspace name", MaxNameLength, name =>
            {
                var trimmed = name?.Trim() ?? string.Empty;
                if (trimmed.Length == 0)
                {
                    return CommandResult.ShowToast(new ToastArgs { Message = "A workspace needs a name", Result = CommandResult.KeepOpen() });
                }

                _store.Rename(_workspaceId, trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed);
                return CommandResult.GoBack();
            }),
        ];
    }
}
