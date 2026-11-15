using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopTiler.Core.Rules;

/// <summary>One app of a workspace: the executable to start (full path) and the desktop its
/// windows go to, kept by id like a window rule's.</summary>
public sealed record WorkspaceEntry(string ImagePath, Guid DesktopId)
{
    /// <summary>The executable's file name, the key windows are matched by.</summary>
    public string App => WindowRules.AppName(ImagePath);
}

/// <summary>A named set of apps, each with its desktop. <paramref name="Id"/> never changes, so a
/// hotkey bound to the workspace's command survives renames.</summary>
public sealed record Workspace(Guid Id, string Name, IReadOnlyList<WorkspaceEntry> Entries)
{
    // Records compare lists by reference; compare entries by value so tests and change checks work.
    public bool Equals(Workspace? other) =>
        other is not null && Id == other.Id && Name == other.Name && Entries.SequenceEqual(other.Entries);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Entries.Count);
}

/// <summary>One open app window, as the snapshot sees it.</summary>
public readonly record struct SnapshotWindow(string ImagePath, Guid DesktopId);

/// <summary>Turning the open windows into a workspace's entries (pure, unit tested).</summary>
public static class WorkspaceSnapshot
{
    /// <param name="windowsInZOrder">Every app window, front first.</param>
    /// <param name="desktopsInOrder">The desktops in display order.</param>
    /// <param name="skippedStoreApps">How many windows were left out because they're Store apps,
    /// which can't be started from a path.</param>
    /// <param name="onlyDesktop">If set, only windows on this desktop are taken.</param>
    /// <returns>One entry per app (its frontmost window decides the desktop), ordered by desktop,
    /// then front to back. Windows on no known desktop (shown on all of them) and windows whose
    /// path can't be read are left out.</returns>
    public static IReadOnlyList<WorkspaceEntry> FromWindows(IEnumerable<SnapshotWindow> windowsInZOrder, IReadOnlyList<Guid> desktopsInOrder, out int skippedStoreApps, Guid? onlyDesktop = null)
    {
        ArgumentNullException.ThrowIfNull(windowsInZOrder);
        ArgumentNullException.ThrowIfNull(desktopsInOrder);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var picked = new List<(WorkspaceEntry Entry, int Desktop, int Z)>();
        var storeApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var z = 0;
        foreach (var w in windowsInZOrder)
        {
            z++;
            var desktop = IndexOf(desktopsInOrder, w.DesktopId);
            var app = WindowRules.AppName(w.ImagePath);
            if (desktop < 0 || app.Length == 0 || (onlyDesktop is { } only && w.DesktopId != only))
            {
                continue;
            }

            if (!WindowRules.CanHaveRule(w.ImagePath))
            {
                storeApps.Add(w.ImagePath);
                continue;
            }

            if (seen.Add(app))
            {
                picked.Add((new WorkspaceEntry(w.ImagePath, w.DesktopId), desktop, z));
            }
        }

        skippedStoreApps = storeApps.Count;
        return [.. picked.OrderBy(p => p.Desktop).ThenBy(p => p.Z).Select(p => p.Entry)];
    }

    /// <summary>"Workspace 1", "Workspace 2", ... - the first not already taken.</summary>
    public static string DefaultName(IEnumerable<string> existingNames)
    {
        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        for (var n = 1; ; n++)
        {
            var name = $"Workspace {n}";
            if (!taken.Contains(name))
            {
                return name;
            }
        }
    }

    private static int IndexOf(IReadOnlyList<Guid> list, Guid id)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == id)
            {
                return i;
            }
        }

        return -1;
    }
}
