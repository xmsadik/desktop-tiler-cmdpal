using System;
using System.Collections.Generic;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// "Manage desktops": a "New desktop" row, then one row per desktop. Choosing a desktop switches
/// to it; its context menu renames, moves, or deletes it. The page stays open after a change and
/// follows the registry poll, so the list reflects each change right away.
/// </summary>
internal sealed partial class ManageDesktopsPage : ListPage, IDisposable
{
    private readonly DesktopManager _manager;
    private readonly VdComClient _vdClient;
    private readonly RegistryDesktopReader _registryReader;
    private readonly DesktopsPage _desktopsPage;

    public ManageDesktopsPage(DesktopManager manager, VdComClient vdClient, RegistryDesktopReader registryReader, DesktopsPage desktopsPage)
    {
        _manager = manager;
        _vdClient = vdClient;
        _registryReader = registryReader;
        _desktopsPage = desktopsPage;

        Id = "DesktopTiler.desktops.manage";
        Name = "Manage desktops";
        Title = "Manage desktops";
        Icon = new IconInfo(""); // ViewAll
        PlaceholderText = "Search desktops; open a row's menu to rename, move or delete it";

        _registryReader.Changed += OnRegistryChanged;
    }

    public void Dispose() => _registryReader.Changed -= OnRegistryChanged;

    public override IListItem[] GetItems()
    {
        // Called directly by the host: must never throw.
        try
        {
            return BuildItems();
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"ManageDesktopsPage.GetItems failed: {ex}");
            return [new ListItem { Title = "Desktop list unavailable" }];
        }
    }

    private IListItem[] BuildItems()
    {
        var desktops = _manager.ReadDesktops();
        Guid currentId;
        try
        {
            currentId = _manager.CurrentId();
        }
        catch (Exception)
        {
            currentId = Guid.Empty; // only the "current" marker is lost
        }

        var items = new List<IListItem>(desktops.Count + 1)
        {
            new ListItem(_manager.NewCommand(follow: false, keepOpen: true))
            {
                Subtitle = "Add a desktop at the end",
                MoreCommands = [new CommandContextItem(_manager.NewCommand(follow: true, keepOpen: false))],
            },
        };

        foreach (var d in desktops)
        {
            items.Add(new ListItem(new SwitchDesktopCommand(_vdClient, d.Id, _desktopsPage) { Name = "Switch to desktop" })
            {
                Title = DesktopManager.Label(d),
                Subtitle = d.Id == currentId ? "Current desktop" : string.Empty,
                MoreCommands = _manager.ContextItems(d.Id, keepOpen: true),
            });
        }

        return [.. items];
    }

    private void OnRegistryChanged(object? sender, EventArgs e)
    {
        try
        {
            RaiseItemsChanged();
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"ManageDesktopsPage refresh failed: {ex.Message}");
        }
    }
}
