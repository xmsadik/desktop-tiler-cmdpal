using System;
using System.Collections.Generic;
using System.IO;

namespace DesktopTiler.Core.Rules;

/// <summary>"New windows of <paramref name="App"/> open on desktop <paramref name="DesktopId"/>".
/// <paramref name="App"/> is the executable's file name (e.g. "OUTLOOK.EXE"), compared
/// case-insensitively; <paramref name="ImagePath"/> is the full path last seen for it, kept only
/// for the rule list's icon. The desktop is kept by its id, which survives reboots, renames and
/// reordering; a rule whose desktop was removed simply does nothing.</summary>
public sealed record WindowRule(string App, Guid DesktopId, string ImagePath);

/// <summary>Pure rule matching, kept apart from the window and COM plumbing so it can be unit
/// tested.</summary>
public static class WindowRules
{
    /// <summary>The executable all UWP / Store app windows report, whatever the app. A rule for it
    /// would catch every such app at once, so none can be made.</summary>
    public const string UwpFrameHost = "ApplicationFrameHost.exe";

    /// <summary>The rule key for a window's executable path: its file name, or empty if the path
    /// couldn't be read.</summary>
    public static string AppName(string imagePath) =>
        string.IsNullOrEmpty(imagePath) ? string.Empty : Path.GetFileName(imagePath);

    /// <summary>Whether a rule can be made for a window of <paramref name="imagePath"/>.</summary>
    public static bool CanHaveRule(string imagePath)
    {
        var app = AppName(imagePath);
        return app.Length > 0 && !string.Equals(app, UwpFrameHost, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The rule for windows of <paramref name="imagePath"/>, if any.</summary>
    public static WindowRule? Find(IReadOnlyList<WindowRule> rules, string imagePath)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var app = AppName(imagePath);
        if (app.Length == 0)
        {
            return null;
        }

        foreach (var rule in rules)
        {
            if (string.Equals(rule.App, app, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>Where a new window of <paramref name="imagePath"/> now on desktop
    /// <paramref name="windowDesktop"/> should go: the rule's desktop, or null to leave it alone -
    /// no rule, already there, the rule's desktop no longer exists, or the window isn't on one
    /// known desktop (e.g. shown on all of them).</summary>
    public static Guid? TargetFor(IReadOnlyList<WindowRule> rules, string imagePath, Guid windowDesktop, IReadOnlyCollection<Guid> desktops) =>
        MoveTarget(Find(rules, imagePath)?.DesktopId, windowDesktop, desktops);

    /// <summary>Whether a window on <paramref name="windowDesktop"/> that should be on
    /// <paramref name="wanted"/> needs moving: <paramref name="wanted"/> if so, null if there's
    /// nothing wanted, it's already there, <paramref name="wanted"/> no longer exists, or the window
    /// isn't on one known desktop (e.g. shown on all of them).</summary>
    public static Guid? MoveTarget(Guid? wanted, Guid windowDesktop, IReadOnlyCollection<Guid> desktops)
    {
        ArgumentNullException.ThrowIfNull(desktops);
        if (wanted is not { } target
            || target == windowDesktop
            || !Contains(desktops, windowDesktop)
            || !Contains(desktops, target))
        {
            return null;
        }

        return target;
    }

    private static bool Contains(IReadOnlyCollection<Guid> desktops, Guid id)
    {
        foreach (var d in desktops)
        {
            if (d == id)
            {
                return true;
            }
        }

        return false;
    }
}
