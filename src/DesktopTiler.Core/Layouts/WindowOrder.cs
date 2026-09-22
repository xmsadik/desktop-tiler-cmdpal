using System.Collections.Generic;
using System.Linq;

namespace DesktopTiler.Core.Layouts;

/// <summary>
/// Identifies a window for order-memory purposes by handle *and* owning process id, not the
/// handle alone - Windows recycles HWND values, so a bare <c>nint</c> match between two tiling
/// passes could otherwise be a stale reference to an entirely different (closed, then
/// coincidentally reused) window. A recycled handle with a different PID compares unequal here,
/// so <see cref="WindowOrder.Resolve"/> correctly treats it as a brand-new window rather than
/// matching it to the old one's position (or, worse, to <see cref="TileMemory.TargetAtRecord"/>).
/// </summary>
public readonly record struct WindowId(nint Handle, int ProcessId);

/// <summary>
/// One tiling pass' resolved window order, remembered so the next pass (Retile / Next layout /
/// Rotate) can preserve manual reordering instead of always resetting to raw Z-order. Pure data -
/// no Win32 - so the caller (Tiler) can read/resolve/apply/record it entirely under its own lock.
/// <paramref name="Order"/> is the full remembered order (target first at the time it was
/// recorded, followed by the rest); <paramref name="TargetAtRecord"/> is which window was the
/// target/master for that pass, used by <see cref="WindowOrder.Resolve"/> to detect a focus
/// change since.
/// </summary>
public sealed record TileMemory(nint Monitor, IReadOnlyList<WindowId> Order, WindowId TargetAtRecord);

/// <summary>
/// Resolves the window order one tiling pass should use, given what (if anything) was
/// remembered from the previous pass on this monitor. Pure and fully unit-tested - no Win32, no
/// I/O; every "live" input (current candidates, current target/monitor, whether this is a
/// Rotate) is passed in already-resolved. Summarized here:
///
/// 1. No usable memory (nothing recorded yet, or it was for a different monitor) - use the raw
///    candidate order as-is (target first, then Z-order - see
///    <see cref="TargetWindowSelector.SelectCandidates"/>).
/// 2. Usable memory - replay its order filtered down to windows still present (closed windows,
///    or a since-reused handle with a different PID, drop out), then append any windows opened
///    since (in their current order). If every remembered window has since closed, that's not
///    usable either - fall back to the raw candidate order.
/// 3. If the user has since focused a window other than the one this memory was recorded for -
///    and other than the window that was already the (possibly just-rotated) master, i.e.
///    <see cref="TileMemory.Order"/>'s first entry - that newly-focused window becomes the new
///    master (moved to index 0). Both count as "no change" so that Retile right after Rotate,
///    with the actual OS focus untouched, reproduces the exact same order instead of being
///    mistaken for the user picking a different window.
/// 4. Rotate then moves index 0 to the end, so whatever was at index 1 becomes the new master.
///    Zero or one window is unaffected.
/// </summary>
public static class WindowOrder
{
    public static IReadOnlyList<WindowId> Resolve(
        TileMemory? memory,
        IReadOnlyList<WindowId> candidates,
        WindowId currentTarget,
        nint currentMonitor,
        bool rotate)
    {
        var baseOrder = ResolveBase(memory, candidates, currentTarget, currentMonitor);

        if (!rotate || baseOrder.Count <= 1)
        {
            return baseOrder;
        }

        var rotated = new List<WindowId>(baseOrder.Count);
        for (var i = 1; i < baseOrder.Count; i++)
        {
            rotated.Add(baseOrder[i]);
        }

        rotated.Add(baseOrder[0]);
        return rotated;
    }

    private static IReadOnlyList<WindowId> ResolveBase(
        TileMemory? memory,
        IReadOnlyList<WindowId> candidates,
        WindowId currentTarget,
        nint currentMonitor)
    {
        if (memory is not { } m || m.Monitor != currentMonitor)
        {
            return candidates;
        }

        var candidateSet = new HashSet<WindowId>(candidates);
        var remembered = m.Order.Where(candidateSet.Contains).ToList();
        if (remembered.Count == 0)
        {
            // Every window this memory knew about is gone - nothing left to replay.
            return candidates;
        }

        var rememberedSet = new HashSet<WindowId>(remembered);
        var merged = remembered.Concat(candidates.Where(id => !rememberedSet.Contains(id))).ToList();

        var recordedMaster = m.Order.Count > 0 ? m.Order[0] : m.TargetAtRecord;
        var noFocusChange = currentTarget == m.TargetAtRecord || currentTarget == recordedMaster;
        if (!noFocusChange)
        {
            var index = merged.IndexOf(currentTarget);
            if (index > 0)
            {
                merged.RemoveAt(index);
                merged.Insert(0, currentTarget);
            }
        }

        return merged;
    }
}
