using System;
using System.Collections.Generic;
using DesktopTiler.Core.VirtualDesktops;

namespace DesktopTiler.Core.Windows;

/// <summary>One app window as gathered live by the caller: its handle, title, executable path
/// (empty if it couldn't be read, e.g. an elevated process) and virtual desktop id
/// (<see cref="Guid.Empty"/> if that couldn't be read).</summary>
public readonly record struct WindowListInput(nint Handle, string Title, string ImagePath, Guid DesktopId);

/// <summary>One row of the "All windows" list. <see cref="Desktop"/> is null for a window that
/// isn't on any desktop in the list (pinned to all desktops, or an id Windows hasn't caught up
/// with) - activating it needs no switch.</summary>
public readonly record struct WindowListEntry(nint Handle, string Title, string ImagePath, DesktopInfo? Desktop);

/// <summary>
/// Pure ordering logic for the "All windows" list: windows grouped by desktop in display order
/// (the registry's <c>VirtualDesktopIDs</c> order), each group keeping the caller's Z-order
/// (front-most first), then windows on no listed desktop last. Windows whose desktop id couldn't
/// be read are dropped, as for the Dock occupancy icons: they are mostly suspended UWP frames
/// that can't be switched to anyway.
/// </summary>
public static class WindowListBuilder
{
    public static IReadOnlyList<WindowListEntry> Build(IReadOnlyList<WindowListInput> windowsInZOrder, IReadOnlyList<DesktopInfo> desktops)
    {
        ArgumentNullException.ThrowIfNull(windowsInZOrder);
        ArgumentNullException.ThrowIfNull(desktops);

        var position = new Dictionary<Guid, int>(desktops.Count);
        for (var i = 0; i < desktops.Count; i++)
        {
            position.TryAdd(desktops[i].Id, i);
        }

        var groups = new List<WindowListEntry>[desktops.Count];
        var unassigned = new List<WindowListEntry>();
        foreach (var w in windowsInZOrder)
        {
            if (w.DesktopId == Guid.Empty)
            {
                continue;
            }

            if (position.TryGetValue(w.DesktopId, out var p))
            {
                (groups[p] ??= []).Add(new WindowListEntry(w.Handle, w.Title, w.ImagePath, desktops[p]));
            }
            else
            {
                unassigned.Add(new WindowListEntry(w.Handle, w.Title, w.ImagePath, null));
            }
        }

        var result = new List<WindowListEntry>(windowsInZOrder.Count);
        foreach (var group in groups)
        {
            if (group is not null)
            {
                result.AddRange(group);
            }
        }

        result.AddRange(unassigned);
        return result;
    }
}
