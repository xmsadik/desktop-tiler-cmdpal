using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DesktopTiler.Core.Rules;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// "Window rules": one row per rule - the app, and the desktop its new windows open on. Choosing
/// a row picks another desktop for it; its context menu deletes it. Rules are made from a
/// window's menu in "All windows" ("Always open on desktop N"), which the empty list says.
/// </summary>
internal sealed partial class WindowRulesPage : ListPage, IDisposable
{
    internal static readonly IconInfo RuleIcon = new(""); // list with an arrow: "goes to"
    private static readonly IconInfo DeleteIcon = new(""); // Delete
    private static readonly IconInfo AppFallbackIcon = new(""); // Window

    private readonly WindowRuleStore _store;
    private readonly DesktopManager _desktops;

    public WindowRulesPage(WindowRuleStore store, DesktopManager desktops)
    {
        _store = store;
        _desktops = desktops;

        Id = "DesktopTiler.rules";
        Name = "Window rules";
        Title = "Window rules";
        Icon = RuleIcon;
        PlaceholderText = "Search window rules";

        _store.Changed += OnRulesChanged;
    }

    public void Dispose() => _store.Changed -= OnRulesChanged;

    public override IListItem[] GetItems()
    {
        // Called directly by the host: must never throw.
        try
        {
            var rules = _store.Rules;
            if (rules.Count == 0)
            {
                return
                [
                    new ListItem(new NoOpCommand())
                    {
                        Title = "No window rules yet",
                        Subtitle = "In All windows, choose \"Always open on desktop N\" from a window's menu (Ctrl+K)",
                        Icon = RuleIcon,
                    },
                ];
            }

            var desktops = _desktops.ReadDesktops();
            return [.. rules.OrderBy(r => r.App, StringComparer.OrdinalIgnoreCase).Select(r => Item(r, desktops))];
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"WindowRulesPage.GetItems failed: {ex}");
            return [new ListItem { Title = "Window rules unavailable" }];
        }
    }

    /// <summary>"Desktop 2 · Mail", or a note that the rule's desktop was deleted.</summary>
    internal static string DescribeTarget(WindowRule rule, IReadOnlyList<DesktopInfo> desktops)
    {
        foreach (var d in desktops)
        {
            if (d.Id == rule.DesktopId)
            {
                return $"Opens on {DesktopManager.Label(d)}";
            }
        }

        return "Its desktop was deleted - choose another (the rule does nothing until then)";
    }

    private ListItem Item(WindowRule rule, IReadOnlyList<DesktopInfo> desktops)
    {
        var app = rule.App;
        var delete = new DesktopActionCommand(null, "Delete rule", DeleteIcon, () =>
        {
            _store.Remove(app);
            return CommandResult.KeepOpen();
        });

        return new ListItem(new RuleDesktopPage(_store, _desktops, rule))
        {
            Title = Path.GetFileNameWithoutExtension(rule.App),
            Subtitle = $"{DescribeTarget(rule, desktops)} · {rule.App}",
            Icon = rule.ImagePath.Length > 0 && File.Exists(rule.ImagePath) ? new IconInfo(rule.ImagePath) : AppFallbackIcon,
            MoreCommands = [new CommandContextItem(delete) { IsCritical = true }],
        };
    }

    private void OnRulesChanged(object? sender, EventArgs e)
    {
        try
        {
            RaiseItemsChanged();
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"WindowRulesPage refresh failed: {ex.Message}");
        }
    }
}

/// <summary>Picks the desktop a rule's app opens on: one row per desktop, the current target
/// marked; choosing one updates the rule and goes back.</summary>
internal sealed partial class RuleDesktopPage : ListPage
{
    private readonly WindowRuleStore _store;
    private readonly DesktopManager _desktops;
    private readonly WindowRule _rule;

    public RuleDesktopPage(WindowRuleStore store, DesktopManager desktops, WindowRule rule)
    {
        _store = store;
        _desktops = desktops;
        _rule = rule;

        Name = "Choose desktop";
        Title = $"Open {Path.GetFileNameWithoutExtension(rule.App)} on";
        Icon = WindowRulesPage.RuleIcon;
        PlaceholderText = "Choose the desktop new windows of this app open on";
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
                    return new ListItem(new DesktopActionCommand(null, "Choose", WindowRulesPage.RuleIcon, () =>
                    {
                        _store.Set(_rule with { DesktopId = target });
                        return CommandResult.GoBack();
                    }))
                    {
                        Title = DesktopManager.Label(d),
                        Subtitle = d.Id == _rule.DesktopId ? "Current choice" : string.Empty,
                    };
                }),
            ];
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"RuleDesktopPage.GetItems failed: {ex}");
            return [new ListItem { Title = "Desktop list unavailable" }];
        }
    }
}
