using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DesktopTiler.Core.Rules;
using DesktopTiler.Core.VirtualDesktops;
using DesktopTiler.Core.Windows;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// "All windows": every app window on every virtual desktop, grouped by desktop in display order
/// (see <see cref="WindowListBuilder"/>). Choosing one switches to its desktop and brings it to
/// the front. Opened from the button at the end of the Dock band, or as a top-level command
/// (so it can have a hotkey). The list is rebuilt each time the host asks for it; the host filters
/// it by the search text itself.
///
/// The host keeps an open page when the palette is hidden and shows it again as it was, without
/// asking for the items again. So the page also raises ItemsChanged whenever the Dock band's
/// desktop page refreshes (<see cref="DesktopsPage.Refreshed"/>): that happens when windows open,
/// close or change desktop, and when the current desktop or a desktop name changes - exactly what
/// this list shows - and the band has already compared the window sets, so no extra scan is needed.
/// </summary>
internal sealed partial class AllWindowsPage : ListPage, IDisposable
{
    private static readonly IconInfo BringHereIcon = new("\uE8A7"); // OpenInNewWindow
    private static readonly IconInfo MoveIcon = new("\uE8DE"); // MoveToFolder
    private static readonly IconInfo CloseIcon = new("\uE8BB"); // ChromeClose
    private static readonly IconInfo PinIcon = new("\uE718"); // Pin
    private static readonly IconInfo UnpinIcon = new("\uE77A"); // Unpin

    // After a move or close from the context menu the page stays open; give the window a moment
    // to actually go before the list is rebuilt.
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(300);

    private static readonly IconInfo WindowFallbackIcon = new(""); // OpenInNewWindow glyph, for windows whose exe path can't be read

    private readonly VdComClient _vdClient;
    private readonly RegistryDesktopReader _registryReader;
    private readonly DesktopsPage _desktopsPage;
    private readonly SettingsManager _settingsManager;
    private readonly WindowRuleStore _rules;

    /// <param name="name">The command name. The Dock button passes an empty one: with no title the
    /// Dock falls back to showing the name, and that button should be icon-only.</param>
    public AllWindowsPage(VdComClient vdClient, RegistryDesktopReader registryReader, DesktopsPage desktopsPage, SettingsManager settingsManager, WindowRuleStore rules, string name = "All windows")
    {
        _vdClient = vdClient;
        _registryReader = registryReader;
        _desktopsPage = desktopsPage;
        _settingsManager = settingsManager;
        _rules = rules;

        Id = "DesktopTiler.windows";
        Name = name;
        Title = "All windows";
        Icon = new IconInfo(""); // TaskView glyph: stacked windows
        PlaceholderText = "Search windows on all desktops";

        _desktopsPage.Refreshed += OnDesktopsRefreshed;
    }

    public void Dispose() => _desktopsPage.Refreshed -= OnDesktopsRefreshed;

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

        // Which windows are shown on all desktops, in one STA hop; a pinned window reports a
        // desktop id that matches no desktop, so it is already in the "All desktops" group.
        var pinned = _vdClient.GetWindowsPinnedAsync(entries.Select(e => e.Handle).ToArray())
            .WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);

        var items = new List<IListItem>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            items.Add(new ListItem(new ActivateWindowCommand(_vdClient, entry.Desktop?.Id, entry.Handle, _desktopsPage))
            {
                Title = entry.Title,
                Subtitle = Describe(entry, currentId),
                Icon = entry.ImagePath.Length > 0 ? new IconInfo(entry.ImagePath) : WindowFallbackIcon,
                MoreCommands = ContextCommands(entry, desktops, currentId, pinned[i]),
            });
        }

        return [.. items];
    }

    /// <summary>The row's context menu: bring the window to this desktop, move it to each other
    /// desktop, show it on all desktops, close it. A pinned window gets "Show only on this
    /// desktop" and "Close window"; a window on no known desktop otherwise only "Close window".</summary>
    private IContextItem[] ContextCommands(WindowListEntry entry, IReadOnlyList<DesktopInfo> desktops, Guid currentId, bool isPinned)
    {
        var window = entry.Handle;
        var commands = new List<IContextItem>();
        if (isPinned)
        {
            commands.Add(new CommandContextItem(new WindowActionCommand("Show only on this desktop", UnpinIcon, () =>
            {
                _vdClient.SetWindowPinnedAsync(window, false).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
                RefreshSoon();
                return CommandResult.KeepOpen();
            })));
        }
        else if (entry.Desktop is { } own)
        {
            if (own.Id != currentId && currentId != Guid.Empty)
            {
                commands.Add(new CommandContextItem(new WindowActionCommand("Bring to this desktop", BringHereIcon, () =>
                {
                    _vdClient.MoveWindowAsync(window, currentId).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
                    _vdClient.ActivateWindowAsync(currentId, window, animate: false).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
                    return CommandResult.Dismiss();
                })));
            }

            foreach (var d in desktops)
            {
                if (d.Id == own.Id)
                {
                    continue;
                }

                var target = d.Id;
                var number = (d.Index + 1).ToString(CultureInfo.InvariantCulture);
                var label = string.IsNullOrEmpty(d.Name) ? $"Move to desktop {number}" : $"Move to desktop {number} · {d.Name}";
                commands.Add(new CommandContextItem(new WindowActionCommand(label, MoveIcon, () =>
                {
                    _vdClient.MoveWindowAsync(window, target).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
                    RefreshSoon();
                    return CommandResult.KeepOpen();
                })));
            }

            commands.Add(new CommandContextItem(new WindowActionCommand("Show on all desktops", PinIcon, () =>
            {
                _vdClient.SetWindowPinnedAsync(window, true).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
                RefreshSoon();
                return CommandResult.KeepOpen();
            })));

            if (RuleCommand(entry.ImagePath, own) is { } rule)
            {
                commands.Add(new CommandContextItem(rule));
            }
        }

        commands.Add(new CommandContextItem(new WindowActionCommand("Close window", CloseIcon, () =>
        {
            WindowEnumerator.RequestClose(window);
            RefreshSoon();
            return CommandResult.KeepOpen();
        }))
        {
            IsCritical = true,
        });

        return [.. commands];
    }

    /// <summary>"Always open on desktop N" (makes or retargets the window's app's rule), or
    /// "Remove window rule" when that rule already exists. The app's name is on the
    /// row already and the menu is narrow, so it's left out here; the toast names both. Null for
    /// windows no rule can be made for (see <see cref="WindowRules.CanHaveRule"/>).</summary>
    private WindowActionCommand? RuleCommand(string imagePath, DesktopInfo own)
    {
        if (!WindowRules.CanHaveRule(imagePath))
        {
            return null;
        }

        var app = WindowRules.AppName(imagePath);
        var shortName = System.IO.Path.GetFileNameWithoutExtension(app);
        var where = DesktopManager.Label(own);
        var number = (own.Index + 1).ToString(CultureInfo.InvariantCulture);
        if (WindowRules.Find(_rules.Rules, imagePath) is { } existing && existing.DesktopId == own.Id)
        {
            return new WindowActionCommand("Remove window rule", WindowRulesPage.RuleIcon, () =>
            {
                _rules.Remove(app);
                return CommandResult.ShowToast(new ToastArgs { Message = $"{shortName} no longer has a window rule", Result = CommandResult.KeepOpen() });
            });
        }

        var target = own.Id;
        return new WindowActionCommand($"Always open on desktop {number}", WindowRulesPage.RuleIcon, () =>
        {
            _rules.Set(new WindowRule(app, target, imagePath));
            return CommandResult.ShowToast(new ToastArgs { Message = $"New {shortName} windows will open on {where}", Result = CommandResult.KeepOpen() });
        });
    }

    private void OnDesktopsRefreshed(object? sender, EventArgs e)
    {
        try
        {
            RaiseItemsChanged();
        }
        catch (Exception ex)
        {
            // Raised on a watcher/poll thread: an escaping exception would end the process.
            SpikeLog.WriteLine($"AllWindowsPage refresh failed: {ex.Message}");
        }
    }

    private void RefreshSoon() => _ = System.Threading.Tasks.Task.Delay(RefreshDelay).ContinueWith(
        _ =>
        {
            try
            {
                RaiseItemsChanged();
            }
            catch (Exception ex)
            {
                SpikeLog.WriteLine($"AllWindowsPage refresh failed: {ex.Message}");
            }
        },
        System.Threading.Tasks.TaskScheduler.Default);

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
