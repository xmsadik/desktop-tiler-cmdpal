using System.Collections.Generic;
using DesktopTiler.Core.Layouts;
using Xunit;

namespace DesktopTiler.Tests;

public class WindowOrderTests
{
    private const nint Monitor1 = 1;
    private const nint Monitor2 = 2;
    private const int DefaultPid = 1;

    private static WindowId W(nint handle, int pid = DefaultPid) => new(handle, pid);

    [Fact]
    public void Resolve_NullMemory_ReturnsCandidatesAsIs()
    {
        IReadOnlyList<WindowId> candidates = [W(1), W(2), W(3)];

        var result = WindowOrder.Resolve(memory: null, candidates, currentTarget: W(1), Monitor1, rotate: false);

        Assert.Equal(candidates, result);
    }

    [Fact]
    public void Resolve_MemoryForDifferentMonitor_ReturnsCandidatesAsIs()
    {
        var memory = new TileMemory(Monitor2, Order: [W(3), W(2), W(1)], TargetAtRecord: W(3));
        IReadOnlyList<WindowId> candidates = [W(1), W(2), W(3)];

        var result = WindowOrder.Resolve(memory, candidates, currentTarget: W(1), Monitor1, rotate: false);

        Assert.Equal(candidates, result);
    }

    [Fact]
    public void Resolve_RotateWithOneCandidate_Unchanged()
    {
        IReadOnlyList<WindowId> candidates = [W(1)];
        var memory = new TileMemory(Monitor1, candidates, TargetAtRecord: W(1));

        var result = WindowOrder.Resolve(memory, candidates, currentTarget: W(1), Monitor1, rotate: true);

        Assert.Equal(candidates, result);
    }

    [Fact]
    public void Resolve_RotateWithNoCandidates_Unchanged()
    {
        IReadOnlyList<WindowId> candidates = [];

        var result = WindowOrder.Resolve(memory: null, candidates, currentTarget: default, Monitor1, rotate: true);

        Assert.Empty(result);
    }

    [Fact]
    public void Resolve_NewWindowSinceMemory_IsAppendedAtTheEnd()
    {
        var memory = new TileMemory(Monitor1, Order: [W(1), W(2)], TargetAtRecord: W(1));
        IReadOnlyList<WindowId> candidates = [W(1), W(2), W(3)]; // 3 opened since

        var result = WindowOrder.Resolve(memory, candidates, currentTarget: W(1), Monitor1, rotate: false);

        Assert.Equal(new[] { W(1), W(2), W(3) }, result);
    }

    [Fact]
    public void Resolve_ClosedWindowSinceMemory_IsDropped()
    {
        var memory = new TileMemory(Monitor1, Order: [W(1), W(2), W(3)], TargetAtRecord: W(1));
        IReadOnlyList<WindowId> candidates = [W(1), W(3)]; // 2 closed since

        var result = WindowOrder.Resolve(memory, candidates, currentTarget: W(1), Monitor1, rotate: false);

        Assert.Equal(new[] { W(1), W(3) }, result);
    }

    [Fact]
    public void Resolve_FocusChangedSinceMemory_MovesNewTargetToFront()
    {
        var memory = new TileMemory(Monitor1, Order: [W(1), W(2), W(3)], TargetAtRecord: W(1));
        IReadOnlyList<WindowId> candidates = [W(1), W(2), W(3)];

        // User has since focused window 3 instead of the recorded target (1), and 3 isn't the
        // recorded master (Order[0] = 1) either - a genuine focus change.
        var result = WindowOrder.Resolve(memory, candidates, currentTarget: W(3), Monitor1, rotate: false);

        Assert.Equal(new[] { W(3), W(1), W(2) }, result);
    }

    [Fact]
    public void Resolve_FocusEqualsTargetAtRecord_KeepsOrder()
    {
        var memory = new TileMemory(Monitor1, Order: [W(1), W(2), W(3)], TargetAtRecord: W(1));
        IReadOnlyList<WindowId> candidates = [W(1), W(2), W(3)];

        var result = WindowOrder.Resolve(memory, candidates, currentTarget: W(1), Monitor1, rotate: false);

        Assert.Equal(new[] { W(1), W(2), W(3) }, result);
    }

    [Fact]
    public void Resolve_FocusEqualsRecordedMasterButNotTargetAtRecord_TreatedAsNoChange()
    {
        // Order[0] (the recorded master) and TargetAtRecord have diverged - this happens right
        // after a Rotate, where TargetAtRecord is still the pre-rotation target (see
        // Resolve_ProductionContract_RepeatedRotateCyclesThenRetileIsStable) but Order[0] is the
        // new, rotated-to master. Focusing that new master must count as "unchanged", not as the
        // user picking yet another window.
        var memory = new TileMemory(Monitor1, Order: [W(2), W(3), W(1)], TargetAtRecord: W(1));
        IReadOnlyList<WindowId> candidates = [W(1), W(2), W(3)];

        var result = WindowOrder.Resolve(memory, candidates, currentTarget: W(2), Monitor1, rotate: false);

        Assert.Equal(new[] { W(2), W(3), W(1) }, result);
    }

    [Fact]
    public void Resolve_EmptyIntersectionWithMemory_FallsBackToCandidates()
    {
        var memory = new TileMemory(Monitor1, Order: [W(10), W(20)], TargetAtRecord: W(10));
        IReadOnlyList<WindowId> candidates = [W(1), W(2), W(3)]; // none of the remembered windows are still open

        var result = WindowOrder.Resolve(memory, candidates, currentTarget: W(1), Monitor1, rotate: false);

        Assert.Equal(candidates, result);
    }

    [Fact]
    public void Resolve_FocusChangedTargetNotInBase_LeavesOrderUnchanged()
    {
        // currentTarget isn't among the resolved base order at all (shouldn't happen in
        // practice - the target is always a candidate - but Resolve must not throw/corrupt).
        var memory = new TileMemory(Monitor1, Order: [W(1), W(2), W(3)], TargetAtRecord: W(1));
        IReadOnlyList<WindowId> candidates = [W(1), W(2), W(3)];

        var result = WindowOrder.Resolve(memory, candidates, currentTarget: W(999), Monitor1, rotate: false);

        Assert.Equal(new[] { W(1), W(2), W(3) }, result);
    }

    [Fact]
    public void Resolve_HandleReusedWithDifferentProcessId_TreatedAsNewWindowNotMatchedToOldTarget()
    {
        // Handle 1 used to belong to pid 100 (the recorded target/master). That process closed
        // and Windows recycled the handle value for an unrelated new window (pid 999), which is
        // now the current target. It must be treated as a brand-new window - appended, then
        // moved to master because it's the live focus - never confused with the stale pid-100
        // "target at record".
        var memory = new TileMemory(Monitor1, Order: [W(1, pid: 100), W(2, pid: 200)], TargetAtRecord: W(1, pid: 100));
        IReadOnlyList<WindowId> candidates = [W(2, pid: 200), W(1, pid: 999)];

        var result = WindowOrder.Resolve(memory, candidates, currentTarget: W(1, pid: 999), Monitor1, rotate: false);

        Assert.Equal(new[] { W(1, pid: 999), W(2, pid: 200) }, result);
    }

    [Fact]
    public void Resolve_ProductionContract_RepeatedRotateCyclesThenRetileIsStable()
    {
        // Mirrors how Tiler.Tile actually calls Resolve: TargetAtRecord is always the live
        // (pre-rotation) target/focus at the time of that tiling pass - not whatever ended up at
        // Order[0] after rotating. With the SW_SHOWNOACTIVATE fix, focus genuinely doesn't move
        // during tiling, so across repeated Rotate presses with no user-initiated focus change,
        // currentTarget stays the same window throughout.
        const int Pid = 1;
        var w1 = W(1, Pid);
        var w2 = W(2, Pid);
        var w3 = W(3, Pid);
        var w4 = W(4, Pid);
        IReadOnlyList<WindowId> candidates = [w1, w2, w3, w4];
        var alwaysFocused = w1;

        var pass1 = WindowOrder.Resolve(memory: null, candidates, alwaysFocused, Monitor1, rotate: true);
        Assert.Equal(new[] { w2, w3, w4, w1 }, pass1);
        var memory1 = new TileMemory(Monitor1, pass1, TargetAtRecord: alwaysFocused);

        var pass2 = WindowOrder.Resolve(memory1, candidates, alwaysFocused, Monitor1, rotate: true);
        Assert.Equal(new[] { w3, w4, w1, w2 }, pass2);
        var memory2 = new TileMemory(Monitor1, pass2, TargetAtRecord: alwaysFocused);

        var pass3 = WindowOrder.Resolve(memory2, candidates, alwaysFocused, Monitor1, rotate: true);
        Assert.Equal(new[] { w4, w1, w2, w3 }, pass3);
        var memory3 = new TileMemory(Monitor1, pass3, TargetAtRecord: alwaysFocused);

        var pass4 = WindowOrder.Resolve(memory3, candidates, alwaysFocused, Monitor1, rotate: true);
        Assert.Equal(candidates, pass4); // back to the start after 4 presses of 4 windows
        var memory4 = new TileMemory(Monitor1, pass4, TargetAtRecord: alwaysFocused);

        // Retile (rotate: false) right after that last Rotate, focus still unchanged, must
        // reproduce the exact same order rather than drifting or un-rotating.
        var retiled = WindowOrder.Resolve(memory4, candidates, alwaysFocused, Monitor1, rotate: false);
        Assert.Equal(pass4, retiled);
    }
}
