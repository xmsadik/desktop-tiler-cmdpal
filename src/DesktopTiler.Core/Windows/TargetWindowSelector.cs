using System;
using System.Collections.Generic;

namespace DesktopTiler.Core.Windows;

/// <summary>
/// Pure target-window and monitor selection, per the Phase 1 tiling design: start from the
/// foreground window; if it's the Command Palette itself, our own process, a desktop-shell
/// window (Progman/WorkerW/Shell_TrayWnd), or not tileable, fall back to the top-most tileable
/// window in Z-order. Comparison is by PID + image name, not window title. No Win32 calls here -
/// everything operates on <see cref="WindowDescriptor"/> so it's fully unit-testable.
/// </summary>
public static class TargetWindowSelector
{
    private static readonly string[] ShellClassNames = ["Progman", "WorkerW", "Shell_TrayWnd"];

    public static bool IsShellClass(string className)
    {
        foreach (var shellClass in ShellClassNames)
        {
            if (string.Equals(shellClass, className, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsExcludedFromConsideration(WindowDescriptor w, int ownProcessId, string cmdPalImageName) =>
        w.ProcessId == ownProcessId
        || string.Equals(w.ImageName, cmdPalImageName, StringComparison.OrdinalIgnoreCase)
        || IsShellClass(w.ClassName);

    public static bool IsTileable(WindowDescriptor w) =>
        w.IsVisible
        && !w.IsToolWindow
        && !w.HasOwner
        && !w.IsMinimized
        && !w.IsCloaked
        && !w.IsHung
        && w.HasThickFrame
        && w.HasTitle
        && w.IsOnCurrentVirtualDesktop;

    /// <summary>
    /// <paramref name="windowsInZOrder"/> must be every top-level window, front (foreground-most)
    /// to back - i.e. the order <c>EnumWindows</c> returns them in. Returns null if there is no
    /// tileable window at all.
    /// </summary>
    public static WindowDescriptor? SelectTarget(
        WindowDescriptor? foreground,
        IReadOnlyList<WindowDescriptor> windowsInZOrder,
        int ownProcessId,
        string cmdPalImageName)
    {
        if (foreground is { } fg
            && !IsExcludedFromConsideration(fg, ownProcessId, cmdPalImageName)
            && IsTileable(fg))
        {
            return fg;
        }

        foreach (var w in windowsInZOrder)
        {
            if (IsExcludedFromConsideration(w, ownProcessId, cmdPalImageName))
            {
                continue;
            }

            if (IsTileable(w))
            {
                return w;
            }
        }

        return null;
    }

    /// <summary>
    /// Tileable windows on the same monitor as <paramref name="target"/>, with
    /// <paramref name="target"/> first (it becomes the tiling master) followed by the rest in
    /// their existing Z-order.
    /// </summary>
    public static IReadOnlyList<WindowDescriptor> SelectCandidates(
        IReadOnlyList<WindowDescriptor> windowsInZOrder,
        WindowDescriptor target)
    {
        var result = new List<WindowDescriptor> { target };
        foreach (var w in windowsInZOrder)
        {
            if (w.Handle == target.Handle)
            {
                continue;
            }

            if (IsTileable(w) && w.MonitorHandle == target.MonitorHandle)
            {
                result.Add(w);
            }
        }

        return result;
    }
}
