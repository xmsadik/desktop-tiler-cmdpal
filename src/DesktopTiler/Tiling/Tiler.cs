using System;
using System.Collections.Generic;
using System.Linq;
using DesktopTiler.Core.Layouts;
using DesktopTiler.Core.VirtualDesktops;
using DesktopTiler.Core.Windows;

namespace DesktopTiler;

/// <summary>Result of one <see cref="Tiler.Tile"/> call: the move/maximize outcome, which
/// <see cref="LayoutKind"/> <paramref name="Kind"/> was actually used (Next/Retile/Rotate choose it
/// dynamically), and the work area of the monitor that was tiled - null if nothing was, e.g. no
/// tileable window - so the layout OSD can be centered on it.</summary>
internal readonly record struct TileOutcome(TileResult Result, LayoutKind Kind, Rect? WorkArea = null);

/// <summary>
/// Ties the Core pieces together for a single "tile now" pass: decide (and record) which layout
/// to use, pick the target window/monitor, enumerate same-monitor tileable candidates, resolve
/// the final window order (remembered order from the previous pass on this virtual
/// desktop+monitor, reconciled with what's live now - see <see cref="WindowOrder"/>), compute the
/// requested layout over the monitor's work area, and move each window once. No
/// continuous/automatic re-tiling - this runs exactly once per command invocation, per the
/// Phase 1 design ("bir kısayol ... pencereleri seçilen düzene bir kez dizer").
/// </summary>
internal static class Tiler
{
    private const int DefaultGap = 8;
    private const double DefaultMasterRatio = 0.55;

    // Serializes tiling passes: two commands invoked back-to-back fast enough to overlap (e.g. a
    // hotkey mashed twice, or "Retile" racing "Next layout") must never enumerate/move the same
    // windows concurrently. selectKind is invoked inside this same lock (not by the caller
    // beforehand) so which layout gets recorded and which one actually gets tiled can never
    // diverge between two overlapping invocations, and the read-resolve-apply-record sequence
    // for the window-order memory below is likewise atomic.
    private static readonly object TileLock = new();

    public static TileOutcome Tile(
        VdComClient vdClient,
        Func<LayoutKind> selectKind,
        TileMemoryStore memoryStore,
        bool rotate = false,
        int gap = DefaultGap,
        double masterRatio = DefaultMasterRatio)
    {
        lock (TileLock)
        {
            var kind = selectKind();

            var gathered = Gather(vdClient);
            if (gathered is not { } g)
            {
                return new TileOutcome(TileResult.Empty, kind);
            }

            var candidateIds = g.Candidates.Select(w => new WindowId(w.Handle, w.ProcessId)).ToList();
            var targetId = new WindowId(g.Target.Handle, g.Target.ProcessId);

            var memory = memoryStore.Get(g.DesktopId, g.Target.MonitorHandle);
            var orderedIds = WindowOrder.Resolve(memory, candidateIds, targetId, g.Target.MonitorHandle, rotate);
            var ordered = Reorder(g.Candidates, orderedIds);

            var result = Apply(kind, ordered, g.WorkArea, gap, masterRatio);

            memoryStore.Record(g.DesktopId, g.Target.MonitorHandle, new TileMemory(g.Target.MonitorHandle, orderedIds, targetId));

            return new TileOutcome(result, kind, g.WorkArea);
        }
    }

    /// <summary>Target window/monitor, same-monitor tileable candidates (target first, then
    /// Z-order), the monitor's work area, and the current virtual desktop id - everything
    /// <see cref="Apply"/> and the window-order memory lookup need, gathered once so all of them
    /// can reuse it.</summary>
    private static Gathered? Gather(VdComClient vdClient)
    {
        var ownProcessId = Environment.ProcessId;
        bool IsOnCurrentDesktop(nint hwnd) => vdClient.IsWindowOnCurrentVirtualDesktopAsync(hwnd).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);

        var foreground = WindowEnumerator.DescribeForeground(IsOnCurrentDesktop);
        var zOrder = WindowEnumerator.EnumerateZOrder(IsOnCurrentDesktop);

        var target = TargetWindowSelector.SelectTarget(foreground, zOrder, ownProcessId, WindowEnumerator.CmdPalImageName);
        if (target is not { } targetWindow)
        {
            return null;
        }

        var candidates = TargetWindowSelector.SelectCandidates(zOrder, targetWindow);
        if (candidates.Count == 0)
        {
            return null;
        }

        var workArea = MonitorWorkArea.Get(targetWindow.MonitorHandle);
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            // MonitorWorkArea.Get failed (e.g. GetMonitorInfo returned false for a monitor
            // that just disconnected) - there's no sane area to tile into, and the layout
            // math below assumes a positive-size work area.
            return null;
        }

        return new Gathered(targetWindow, candidates, workArea, GetCurrentDesktopIdOrDefault(vdClient));
    }

    /// <summary>The current virtual desktop id, used only to scope the window-order memory per
    /// desktop (see <see cref="TileMemoryStore"/>) - best-effort. If the VD COM layer is
    /// unsupported on this build, or any other failure occurs, the memory falls back to one
    /// shared slot per monitor (<see cref="Guid.Empty"/>) rather than failing the whole tiling
    /// pass over what's purely a "remember where windows were" nicety.</summary>
    private static Guid GetCurrentDesktopIdOrDefault(VdComClient vdClient)
    {
        try
        {
            return vdClient.GetCurrentIdAsync().WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
        }
        catch (Exception)
        {
            // Includes ExplorerNotRespondingException: falls back to the shared Guid.Empty slot,
            // same as every other failure this best-effort lookup already tolerates.
            return Guid.Empty;
        }
    }

    /// <summary>Moves/maximizes <paramref name="ordered"/> (already resolved into final tiling
    /// order via <see cref="WindowOrder.Resolve"/>, so <paramref name="ordered"/>[0] is the
    /// current master) into <paramref name="kind"/>'s layout.</summary>
    private static TileResult Apply(LayoutKind kind, List<WindowDescriptor> ordered, Rect workArea, int gap, double masterRatio)
    {
        if (kind == LayoutKind.Monocle)
        {
            // Monocle has no rects to move to - every window is maximized instead (see
            // WindowMover.MaximizeAll), which also settles its own final Z-order, so no
            // IsMaximized/IsZoomed probe is needed for these placements.
            var monoclePlacements = ordered
                .Select(window => new WindowPlacement(
                    window.Handle,
                    window.IsHung,
                    window.HasThickFrame,
                    IsMaximized: false,
                    TargetRect: default))
                .ToList();

            return WindowMover.MaximizeAll(monoclePlacements);
        }

        var rects = LayoutEngine.Compute(kind, workArea, ordered.Count, gap, masterRatio);

        var placements = ordered
            .Zip(rects, (window, rect) => new WindowPlacement(
                window.Handle,
                window.IsHung,
                window.HasThickFrame,
                IsMaximized: MonitorWorkArea.IsZoomed(window.Handle),
                rect))
            .ToList();

        return WindowMover.Move(placements);
    }

    /// <summary><paramref name="order"/> is always a permutation of <paramref name="candidates"/>'
    /// identities (WindowOrder.Resolve only ever reorders, never adds/removes), so this lookup
    /// can't miss. Handles are unique within one Gather pass, so matching on the handle alone is
    /// enough here even though the memory itself keys on (handle, pid).</summary>
    private static List<WindowDescriptor> Reorder(IReadOnlyList<WindowDescriptor> candidates, IReadOnlyList<WindowId> order)
    {
        var byHandle = candidates.ToDictionary(w => w.Handle);
        return order.Select(id => byHandle[id.Handle]).ToList();
    }

    private readonly record struct Gathered(WindowDescriptor Target, IReadOnlyList<WindowDescriptor> Candidates, Rect WorkArea, Guid DesktopId);
}
