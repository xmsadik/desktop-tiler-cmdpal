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
/// The "Desktops" Dock band content: one <see cref="ListItem"/> per virtual desktop, title
/// "1"/"2"/... with the active desktop marked "●N", subtitle = the desktop's name if it has one.
/// Clicking an item switches to that desktop. Each item's icon is a filled square if the desktop
/// has at least one app window on it, an outlined square if it's empty (no icon at all if that
/// couldn't be determined); <see cref="WindowEventWatcher"/> keeps it live as windows come and go.
///
/// Also usable as an ordinary <see cref="ListPage"/> (so it can be opened as a page, not just as
/// a Dock band), which is why it implements <see cref="ListPage.RaiseItemsChanged"/> semantics.
/// The provider additionally listens to <see cref="Refreshed"/> to keep the separately-created
/// <c>WrappedDockItem.Items</c> array (the actual Dock band content) in sync, since
/// RaiseItemsChanged only notifies consumers of this page, not of the Dock band wrapper.
/// </summary>
internal sealed partial class DesktopsPage : ListPage, IDisposable
{
    private static readonly IconInfo OccupiedIcon = new(""); // CheckboxFill
    private static readonly IconInfo EmptyIcon = new(""); // Checkbox

    private readonly VdComClient _vdClient;
    private readonly RegistryDesktopReader _registryReader;
    private readonly SettingsManager _settingsManager;
    private readonly WindowEventWatcher _windowWatcher;

    // The occupancy set the Dock last rendered (written by GetItems). Window events recompute it
    // and only refresh the band when it actually differs, so the constant churn of unrelated
    // windows showing/hiding doesn't redraw the band. Replaced, never mutated.
    private volatile HashSet<Guid>? _lastOccupied;

    public DesktopsPage(VdComClient vdClient, RegistryDesktopReader registryReader, SettingsManager settingsManager, WindowEventWatcher windowWatcher)
    {
        _vdClient = vdClient;
        _registryReader = registryReader;
        _settingsManager = settingsManager;
        _windowWatcher = windowWatcher;

        Id = "DesktopTiler.page.desktops";
        Title = "Desktops";
        Name = "Desktops";
        Icon = new IconInfo(""); // Desktop-ish glyph fallback; keep simple for Phase 1.
        PlaceholderText = "Switch virtual desktop";

        _registryReader.Changed += OnRegistryChanged;
        _settingsManager.ShowDesktopNamesChanged += OnShowDesktopNamesChanged;
        _windowWatcher.Changed += OnWindowsChanged;
    }

    /// <summary>Raised whenever the item set has changed (registry watcher fired, a switch we just
    /// performed, or the "Show desktop names" setting changed), in addition to the standard
    /// RaiseItemsChanged.</summary>
    public event EventHandler? Refreshed;

    public void Dispose()
    {
        _registryReader.Changed -= OnRegistryChanged;
        _settingsManager.ShowDesktopNamesChanged -= OnShowDesktopNamesChanged;
        _windowWatcher.Changed -= OnWindowsChanged;
    }

    /// <summary>Called by the switch command right after a successful switch, since the COM
    /// "current desktop" can change without the registry key changing in lockstep.</summary>
    public void NotifyAfterSwitch() => Refresh();

    public override IListItem[] GetItems()
    {
        IReadOnlyList<DesktopInfo> desktops;
        try
        {
            desktops = _registryReader.ReadDesktops();
        }
        catch (Exception ex)
        {
            // Called directly by the host (not just via Refresh()), so this must never throw -
            // an unhandled exception here would crash the host, not just this page.
            SpikeLog.WriteLine($"DesktopsPage.GetItems: ReadDesktops failed: {ex}");
            return [new ListItem { Title = "Desktop list unavailable" }];
        }

        if (desktops.Count == 0)
        {
            // A fresh profile that's never opened Task View has no VirtualDesktops registry key
            // at all, even though Windows always has (at least) one desktop - fall back to the
            // COM enumeration for that single desktop (no name) before giving up.
            desktops = DesktopListReader.FallbackToVdComClient(_vdClient);
            if (desktops.Count == 0)
            {
                return [new ListItem { Title = "No virtual desktops found" }];
            }
        }

        Guid currentId;
        try
        {
            currentId = _vdClient.GetCurrentIdAsync().WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
        }
        catch (UnsupportedBuildException)
        {
            return [new ListItem { Title = "Unsupported Windows build for desktop switching" }];
        }
        catch (Exception)
        {
            // Includes ExplorerNotRespondingException: no active-desktop marker is a reasonable
            // degrade rather than failing the whole list over a call that timed out.
            currentId = Guid.Empty;
        }

        var occupied = ComputeOccupied();
        _lastOccupied = occupied;

        var items = new IListItem[desktops.Count];
        for (var i = 0; i < desktops.Count; i++)
        {
            var desktop = desktops[i];
            var number = i + 1;
            var isActive = desktop.Id == currentId;
            var title = isActive
                ? "●" + number.ToString(CultureInfo.InvariantCulture)
                : number.ToString(CultureInfo.InvariantCulture);

            items[i] = new ListItem(new SwitchDesktopCommand(_vdClient, desktop.Id, this)
            {
                Name = $"Desktop {number}",
            })
            {
                Title = title,
                Icon = occupied is null ? null : occupied.Contains(desktop.Id) ? OccupiedIcon : EmptyIcon,
                Subtitle = _settingsManager.ShowDesktopNames ? desktop.Name ?? string.Empty : string.Empty,
            };
        }

        return items;
    }

    private void OnRegistryChanged(object? sender, EventArgs e) => Refresh();

    private void OnShowDesktopNamesChanged(object? sender, EventArgs e) => Refresh();

    private void OnWindowsChanged(object? sender, EventArgs e)
    {
        var occupied = ComputeOccupied();
        var last = _lastOccupied;
        if (occupied is not null && last is not null && occupied.SetEquals(last))
        {
            return;
        }

        Refresh();
    }

    /// <summary>The ids of the desktops that have at least one app window on them, or null if
    /// that couldn't be determined (logged; the band then just shows no icons).</summary>
    private HashSet<Guid>? ComputeOccupied()
    {
        try
        {
            using var timing = SpikeLog.Timed("DesktopsPage.ComputeOccupied");
            var ownPid = Environment.ProcessId;
            var handles = WindowEnumerator.EnumerateZOrder(null)
                .Where(w => DesktopOccupancy.IsAppWindow(w, ownPid, WindowEnumerator.CmdPalImageName))
                .Select(w => w.Handle)
                .ToArray();
            var ids = _vdClient.GetWindowDesktopIdsAsync(handles).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
            return DesktopOccupancy.OccupiedDesktops(ids);
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"DesktopsPage.ComputeOccupied failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Raises <see cref="RaiseItemsChanged"/> and <see cref="Refreshed"/>. Called from the
    /// registry watcher's debounce timer thread (<see cref="OnRegistryChanged"/>) as well as
    /// straight off a command invocation (<see cref="NotifyAfterSwitch"/>); either way, an
    /// exception here (ours, a subscriber's, e.g. the provider re-reading items for the Dock band)
    /// must not crash the host - it's logged to SpikeLog and swallowed instead.</summary>
    private void Refresh()
    {
        try
        {
            RaiseItemsChanged();
            Refreshed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"DesktopsPage.Refresh failed: {ex}");
        }
    }
}
