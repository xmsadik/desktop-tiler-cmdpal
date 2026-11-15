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
}
