using System;
using System.Collections.Generic;
using System.Linq;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// "Recent desktops": every desktop, most recently used first (the current one on top), like
/// Alt+Tab's order for windows; desktops not visited since the extension started come last, in
/// their own order. Choosing one switches to it. Follows the registry poll, so the order is
/// current whenever the page is shown.
/// </summary>
internal sealed partial class RecentDesktopsPage : ListPage, IDisposable
{
    private readonly DesktopManager _manager;
    private readonly VdComClient _vdClient;
    private readonly RegistryDesktopReader _registryReader;
    private readonly DesktopsPage _desktopsPage;
    private readonly DesktopHistory _history;

    public RecentDesktopsPage(DesktopManager manager, VdComClient vdClient, RegistryDesktopReader registryReader, DesktopsPage desktopsPage, DesktopHistory history)
    {
        _manager = manager;
        _vdClient = vdClient;
        _registryReader = registryReader;
        _desktopsPage = desktopsPage;
        _history = history;

        Id = "DesktopTiler.desktops.recent";
        Name = "Recent desktops";
        Title = "Recent desktops";
        Icon = new IconInfo(""); // History
        PlaceholderText = "Desktops, most recently used first; choose one to switch to it";

        _registryReader.Changed += OnRegistryChanged;
    }

    public void Dispose() => _registryReader.Changed -= OnRegistryChanged;

    public override IListItem[] GetItems()
    {
        // Called directly by the host: must never throw.
        try
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

            var byId = desktops.ToDictionary(d => d.Id);
            var ordered = _history.Order([.. desktops.Select(d => d.Id)]);

            // The history learns of a switch from the registry poll, which can trail the switch
            // itself; the desktop COM reports as current always goes first.
            if (currentId != Guid.Empty && ordered.Contains(currentId))
            {
                ordered = [currentId, .. ordered.Where(id => id != currentId)];
            }

            var items = new List<IListItem>(ordered.Count);
            for (var i = 0; i < ordered.Count; i++)
            {
                var d = byId[ordered[i]];
                items.Add(new ListItem(new SwitchDesktopCommand(_vdClient, d.Id, _desktopsPage) { Name = "Switch to desktop" })
                {
                    Title = DesktopManager.Label(d),
                    Subtitle = d.Id == currentId ? "Current desktop" : i == 1 && currentId != Guid.Empty ? "Last used" : string.Empty,
                });
            }

            return [.. items];
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"RecentDesktopsPage.GetItems failed: {ex}");
            return [new ListItem { Title = "Desktop list unavailable" }];
        }
    }

    private void OnRegistryChanged(object? sender, EventArgs e)
    {
        try
        {
            RaiseItemsChanged();
        }
        catch (Exception ex)
        {
            // Raised on the poll thread: an escaping exception would end the process.
            SpikeLog.WriteLine($"RecentDesktopsPage refresh failed: {ex.Message}");
        }
    }
}
