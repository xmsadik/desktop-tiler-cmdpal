using System.Collections.Generic;
using System.Runtime.InteropServices;
using DesktopTiler.Core.Layouts;

namespace DesktopTiler.Core.Windows;

/// <summary>
/// Moves each window individually via <c>SetWindowPos</c> - deliberately not
/// <c>DeferWindowPos</c>, since one bad HWND in a deferred batch cancels the whole group. For
/// each window: restore synchronously if maximized, measure the invisible-border inset via
/// <c>DWMWA_EXTENDED_FRAME_BOUNDS</c> vs the raw window rect (measured *after* the restore, so
/// the inset reflects the window's non-maximized frame), then move with
/// SWP_NOZORDER|SWP_NOACTIVATE|SWP_NOOWNERZORDER|SWP_ASYNCWINDOWPOS.
/// </summary>
public static class WindowMover
{
    private const int ErrorAccessDenied = 5;

    public static TileResult Move(IReadOnlyList<WindowPlacement> placements)
    {
        var tiled = 0;
        var skippedAdmin = 0;
        var skippedOther = 0;

        foreach (var placement in placements)
        {
            if (placement.IsHung || !placement.HasThickFrame)
            {
                skippedOther++;
                continue;
            }

            if (placement.IsMaximized)
            {
                // SW_SHOWNOACTIVATE, not SW_RESTORE: SW_RESTORE activates the window, so
                // restoring several previously-maximized windows in this loop would leave
                // whichever one was restored *last* as the new foreground window - not the
                // intended master - and the next tiling pass would then misread that as a
                // deliberate focus change (see WindowOrder.Resolve). SW_SHOWNOACTIVATE restores
                // a maximized window to its normal size/position the same way, without moving
                // activation.
                NativeMethods.ShowWindow(placement.Handle, NativeMethods.SwShowNoActivate);
            }

            var inset = MeasureFrameInset(placement.Handle);
            var adjusted = new Rect(
                placement.TargetRect.Left - inset.Left,
                placement.TargetRect.Top - inset.Top,
                placement.TargetRect.Right + inset.Right,
                placement.TargetRect.Bottom + inset.Bottom);

            var moved = NativeMethods.SetWindowPos(
                placement.Handle,
                0,
                adjusted.Left,
                adjusted.Top,
                adjusted.Width,
                adjusted.Height,
                NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder | NativeMethods.SwpAsyncWindowPos);

            if (moved)
            {
                tiled++;
                continue;
            }

            var error = Marshal.GetLastWin32Error();
            if (error == ErrorAccessDenied)
            {
                skippedAdmin++;
            }
            else
            {
                skippedOther++;
            }
        }

        return new TileResult(tiled, skippedAdmin, skippedOther);
    }

    /// <summary>
    /// Monocle: maximizes every candidate instead of moving it to a rect. Windows are maximized
    /// in reverse order so <c>placements[0]</c> (the target/foreground window) is maximized
    /// *last* - <c>ShowWindow(SW_MAXIMIZE)</c> also raises the window in Z-order, so this alone
    /// usually already leaves it on top - and then unconditionally moved to <c>HWND_TOP</c>
    /// afterwards so it deterministically ends up topmost regardless of maximize order or
    /// timing. <c>ShowWindow</c>'s own return value reports whether the window was *previously*
    /// visible, not whether the call succeeded, so unlike <see cref="Move"/> it isn't a usable
    /// success signal here - <c>IsZoomed</c> after the call is checked instead, and only that
    /// counts a window as tiled.
    /// </summary>
    public static TileResult MaximizeAll(IReadOnlyList<WindowPlacement> placements)
    {
        var tiled = 0;
        var skippedOther = 0;

        for (var i = placements.Count - 1; i >= 0; i--)
        {
            var placement = placements[i];
            if (placement.IsHung || !placement.HasThickFrame)
            {
                skippedOther++;
                continue;
            }

            NativeMethods.ShowWindow(placement.Handle, NativeMethods.SwMaximize);
            if (NativeMethods.IsZoomed(placement.Handle))
            {
                tiled++;
            }
            else
            {
                skippedOther++;
            }
        }

        if (placements.Count > 0)
        {
            var target = placements[0];
            if (!target.IsHung && target.HasThickFrame)
            {
                // HWND_TOP = 0. Position/size are ignored (SWP_NOMOVE|SWP_NOSIZE) - this call is
                // purely to fix the target's Z-order deterministically.
                NativeMethods.SetWindowPos(
                    target.Handle,
                    0,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
            }
        }

        return new TileResult(tiled, SkippedAdmin: 0, skippedOther);
    }

    /// <summary>Left/Top/Right/Bottom insets between the raw window rect and its DWM extended
    /// frame bounds (the raw rect normally has a few pixels of invisible resize border outside
    /// the visible frame). Returns all zeros if DWM can't report the extended frame (e.g. the
    /// window vanished), in which case the target rect is used as-is.</summary>
    private static Rect MeasureFrameInset(nint hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var raw))
        {
            return default;
        }

        var hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaExtendedFrameBounds, out Rect frame, Marshal.SizeOf<Rect>());
        if (hr != 0)
        {
            return default;
        }

        return new Rect(
            frame.Left - raw.Left,
            frame.Top - raw.Top,
            raw.Right - frame.Right,
            raw.Bottom - frame.Bottom);
    }
}
