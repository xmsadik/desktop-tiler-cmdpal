using System;
using DesktopTiler.Core.VirtualDesktops;
using Xunit;

namespace DesktopTiler.Tests;

public class DesktopHistoryTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    [Fact]
    public void NoSwitchSeen_PreviousIsNull()
    {
        var h = new DesktopHistory();
        h.Observe(A);
        h.Observe(A);

        Assert.Null(h.Previous);
    }

    [Fact]
    public void Switch_MakesOldCurrentPrevious()
    {
        var h = new DesktopHistory();
        h.Observe(A);
        h.Observe(B);

        Assert.Equal(A, h.Previous);
    }

    [Fact]
    public void FlippingBack_SwapsTheTwo()
    {
        var h = new DesktopHistory();
        h.Observe(A);
        h.Observe(B);
        h.Observe(A);

        Assert.Equal(B, h.Previous);
    }

    [Fact]
    public void ThirdDesktop_PreviousIsTheOneJustLeft()
    {
        var h = new DesktopHistory();
        h.Observe(A);
        h.Observe(B);
        h.Observe(C);

        Assert.Equal(B, h.Previous);
    }

    [Fact]
    public void EmptyIdAndRepeats_AreIgnored()
    {
        var h = new DesktopHistory();
        h.Observe(A);
        h.Observe(Guid.Empty);
        h.Observe(B);
        h.Observe(B);

        Assert.Equal(A, h.Previous);
    }

    private static readonly Guid D = Guid.NewGuid();

    [Fact]
    public void Order_MostRecentFirst_UnvisitedLastInDisplayOrder()
    {
        var h = new DesktopHistory();
        h.Observe(C);
        h.Observe(A);
        h.Observe(C); // C back on top; A is now second

        Assert.Equal([C, A, B, D], h.Order([A, B, C, D]));
    }

    [Fact]
    public void Order_LeavesOutDeletedDesktops()
    {
        var h = new DesktopHistory();
        h.Observe(A);
        h.Observe(B);
        h.Observe(C);

        Assert.Equal([C, A], h.Order([A, C])); // B was deleted
    }

    [Fact]
    public void PreviousAmong_SkipsDeletedDesktops()
    {
        var h = new DesktopHistory();
        h.Observe(A);
        h.Observe(B);
        h.Observe(C);

        Assert.Equal(B, h.PreviousAmong([A, B, C]));
        Assert.Equal(A, h.PreviousAmong([A, C])); // B deleted: the one before it
        Assert.Null(h.PreviousAmong([C]));
        Assert.Null(new DesktopHistory().PreviousAmong([A]));
    }

    [Fact]
    public void History_KeepsAtMostCapacityDesktops()
    {
        var h = new DesktopHistory();
        var ids = new Guid[DesktopHistory.Capacity + 5];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = Guid.NewGuid();
            h.Observe(ids[i]);
        }

        var order = h.Order(ids);

        Assert.Equal(ids[^1], order[0]);
        Assert.Equal(ids[^DesktopHistory.Capacity], order[DesktopHistory.Capacity - 1]); // oldest remembered
        Assert.Equal(ids[0], order[DesktopHistory.Capacity]); // forgotten: back in display order
    }
}
