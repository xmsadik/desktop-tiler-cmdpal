using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DesktopTiler.Core.VirtualDesktops;
using DesktopTiler.Core.Windows;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// "All windows": every app window on every virtual desktop, grouped by desktop in display order
/// (see <see cref="WindowListBuilder"/>). Choosing one switches to its desktop and brings it to
/// the front. Opened from the button at the end of the Dock band, or as a top-level command
/// (so it can have a hotkey). The list is rebuilt each time the host asks for it, so it's
/// current whenever the page is opened; the host filters it by the search text itself.
/// </summary>
internal sealed partial class AllWindowsPage : ListPage
{
    private static readonly IconInfo WindowFallbackIcon = new(""); // OpenInNewWindow glyph, for windows whose exe path can't be read

    private readonly VdComClient _vdClient;
    private readonly RegistryDesktopReader _registryReader;
    private readonly DesktopsPage _desktopsPage;
    private readonly SettingsManager _settingsManager;

    /// <param name="name">The command name. The Dock button passes an empty one: with no title the
    /// Dock falls back to showing the name, and that button should be icon-only.</param>
    public AllWindowsPage(VdComClient vdClient, RegistryDesktopReader registryReader, DesktopsPage desktopsPage, SettingsManager settingsManager, string name = "All windows")
    {
        _vdClient = vdClient;
        _registryReader = registryReader;
        _desktopsPage = desktopsPage;
        _settingsManager = settingsManager;

        Id = "DesktopTiler.windows";
        Name = name;
        Title = "All windows";
        Icon = new IconInfo(""); // TaskView glyph: stacked windows
        PlaceholderText = "Search windows on all desktops";
    }

    public override IListItem[] GetItems()
    {
        // Called directly by the host: must never throw.
        try
        {
            using var timing = SpikeLog.Timed("AllWindowsPage.GetItems");
            return BuildItems();
        }
        catch (UnsupportedBuildException)
        {
            return [new ListItem { Title = "Unsupported Windows build for desktop switching" }];
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"AllWindowsPage.GetItems failed: {ex}");
            return [new ListItem { Title = "Window list unavailable" }];
        }
    }

    private IListItem[] BuildItems()
    {
        var desktops = _registryReader.ReadDesktops();
        if (desktops.Count == 0)
        {
            desktops = DesktopListReader.FallbackToVdComClient(_vdClient);
        }

        var ownPid = Environment.ProcessId;
        var windows = WindowEnumerator.EnumerateZOrder(null)
            .Where(w => DesktopOccupancy.IsAppWindow(w, ownPid, WindowEnumerator.CmdPalImageName))
            .ToArray();
        var ids = _vdClient.GetWindowDesktopIdsAsync(windows.Select(w => w.Handle).ToArray())
            .WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);

        var inputs = new WindowListInput[windows.Length];
        for (var i = 0; i < windows.Length; i++)
        {
            var w = windows[i];
            inputs[i] = new WindowListInput(w.Handle, WindowEnumerator.GetTitle(w.Handle), WindowEnumerator.GetImagePath(w.ProcessId), ids[i]);
        }

        var entries = WindowListBuilder.Build(inputs, desktops);
        if (entries.Count == 0)
        {
            return [new ListItem { Title = "No open windows" }];
        }

        Guid currentId;
        try
        {
            currentId = _vdClient.GetCurrentIdAsync().WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
        }
        catch (Exception)
        {
            currentId = Guid.Empty; // only the "current" marker is lost
        }

        var items = new List<IListItem>(entries.Count);
        foreach (var entry in entries)
        {
            items.Add(new ListItem(new ActivateWindowCommand(_vdClient, entry.Desktop?.Id, entry.Handle, _desktopsPage))
            {
                Title = entry.Title,
                Subtitle = Describe(entry, currentId),
                Icon = entry.ImagePath.Length > 0 ? new IconInfo(entry.ImagePath) : WindowFallbackIcon,
            });
        }

        return [.. items];
    }

    private string Describe(WindowListEntry entry, Guid currentId)
    {
        var app = System.IO.Path.GetFileNameWithoutExtension(entry.ImagePath);
        string where;
        if (entry.Desktop is { } d)
        {
            var number = (d.Index + 1).ToString(CultureInfo.InvariantCulture);
            var marker = d.Id == currentId ? "●" : string.Empty;
            where = _settingsManager.ShowDesktopNames && !string.IsNullOrEmpty(d.Name)
                ? $"{marker}Desktop {number} · {d.Name}"
                : $"{marker}Desktop {number}";
        }
        else
        {
            where = "All desktops";
        }

        return string.IsNullOrEmpty(app) ? where : $"{where} · {app}";
    }
}
