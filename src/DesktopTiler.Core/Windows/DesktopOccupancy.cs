using System;
using System.Collections.Generic;

namespace DesktopTiler.Core.Windows;

/// <summary>
/// Pure "which virtual desktops have an app window on them" logic for the Dock band's
/// occupied/empty icons. No Win32 calls here - the live window list and per-window desktop ids
/// are gathered by the caller, so this is fully unit-testable.
/// </summary>
public static class DesktopOccupancy
{
    /// <summary>
    /// Whether <paramref name="w"/> counts as an app window for occupancy. Deliberately looser
    /// than <see cref="TargetWindowSelector.IsTileable"/>: a minimized window still means an app
    /// is running on that desktop, and every window on a non-current desktop is DWM-cloaked, so
    /// neither minimized nor cloaked windows are excluded.
    /// </summary>
    public static bool IsAppWindow(WindowDescriptor w, int ownProcessId, string cmdPalImageName) =>
        w.IsVisible
        && !w.IsToolWindow
        && !w.HasOwner
        && w.HasTitle
        && !TargetWindowSelector.IsExcludedFromConsideration(w, ownProcessId, cmdPalImageName);

    /// <summary>The set of desktop ids that at least one app window is on. <see cref="Guid.Empty"/>
    /// (the id could not be read - a window that closed mid-query, or a suspended/background UWP
    /// window) is ignored, as is any id that matches no desktop in the caller's list.</summary>
    public static HashSet<Guid> OccupiedDesktops(IEnumerable<Guid> windowDesktopIds)
    {
        ArgumentNullException.ThrowIfNull(windowDesktopIds);

        var occupied = new HashSet<Guid>();
        foreach (var id in windowDesktopIds)
        {
            if (id != Guid.Empty)
            {
                occupied.Add(id);
            }
        }

        return occupied;
    }
}
