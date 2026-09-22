using DesktopTiler.Core.Layouts;
using Xunit;

namespace DesktopTiler.Tests;

public class CenterMasterLayoutTests
{
    [Fact]
    public void Compute_ZeroWindows_ReturnsEmpty()
    {
        Assert.Empty(CenterMasterLayout.Compute(new Rect(0, 0, 1920, 1040), 0, 8, 0.5));
    }

    [Fact]
    public void Compute_OneWindow_FillsWorkAreaMinusGapMargins()
    {
        var workArea = new Rect(0, 0, 1920, 1040);
        var result = CenterMasterLayout.Compute(workArea, 1, 8, 0.55);

        var cell = Assert.Single(result);
        Assert.Equal(new Rect(8, 8, 1912, 1032), cell);
    }

    [Fact]
    public void Compute_TwoWindows_IsIdenticalToMasterStack()
    {
        var workArea = new Rect(0, 0, 1923, 1041);
        var centerMaster = CenterMasterLayout.Compute(workArea, 2, 8, 0.55);
        var masterStack = MasterStackLayout.Compute(workArea, 2, 8, 0.55);

        Assert.Equal(masterStack, centerMaster);
    }

    [Fact]
    public void Compute_ThreeWindows_NoGap_MasterCenteredFirstStackWindowOnRight()
    {
        var workArea = new Rect(0, 0, 300, 100);
        var result = CenterMasterLayout.Compute(workArea, 3, 0, 0.5);

        Assert.Equal(3, result.Length);
        Assert.Equal(new Rect(75, 0, 225, 100), result[0]); // master, centered
        Assert.Equal(new Rect(225, 0, 300, 100), result[1]); // index 1 -> right column
        Assert.Equal(new Rect(0, 0, 75, 100), result[2]); // index 2 -> left column
    }

    [Fact]
    public void Compute_FourWindows_NoGap_AlternatesRightLeftRight()
    {
        var workArea = new Rect(0, 0, 300, 100);
        var result = CenterMasterLayout.Compute(workArea, 4, 0, 0.5);

        Assert.Equal(4, result.Length);
        Assert.Equal(new Rect(75, 0, 225, 100), result[0]); // master
        Assert.Equal(new Rect(225, 0, 300, 50), result[1]); // right, top
        Assert.Equal(new Rect(0, 0, 75, 100), result[2]); // left (only stack window on this side)
        Assert.Equal(new Rect(225, 50, 300, 100), result[3]); // right, bottom
    }

    [Fact]
    public void Compute_FiveWindows_NoGap_BothSidesGetTwoStackedWindows()
    {
        var workArea = new Rect(0, 0, 300, 100);
        var result = CenterMasterLayout.Compute(workArea, 5, 0, 0.5);

        Assert.Equal(5, result.Length);
        Assert.Equal(new Rect(75, 0, 225, 100), result[0]); // master
        Assert.Equal(new Rect(225, 0, 300, 50), result[1]); // right, top
        Assert.Equal(new Rect(0, 0, 75, 50), result[2]); // left, top
        Assert.Equal(new Rect(225, 50, 300, 100), result[3]); // right, bottom
        Assert.Equal(new Rect(0, 50, 75, 100), result[4]); // left, bottom
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Compute_WithGap_OddArea_TilesValidly(int n)
    {
        var workArea = new Rect(0, 0, 1923, 1041);
        var result = CenterMasterLayout.Compute(workArea, n, 8, 0.55);

        Assert.Equal(n, result.Length);
        LayoutAssert.IsValidTiling(workArea, 8, result);
        LayoutAssert.BoundingBoxMatchesMargin(workArea, 8, result);
    }

    [Fact]
    public void Compute_RatioIsClamped_SameAsMasterStack()
    {
        var workArea = new Rect(0, 0, 300, 100);
        var clamped = CenterMasterLayout.Compute(workArea, 3, 0, 0.9);
        var tooHigh = CenterMasterLayout.Compute(workArea, 3, 0, 50.0);

        Assert.Equal(clamped, tooHigh);
    }

    [Fact]
    public void Compute_NegativeOriginWorkArea_TilesValidly()
    {
        var workArea = new Rect(-1920, -200, 0, 880);
        var result = CenterMasterLayout.Compute(workArea, 5, 8, 0.55);

        Assert.Equal(5, result.Length);
        LayoutAssert.IsValidTiling(workArea, 8, result);
        LayoutAssert.BoundingBoxMatchesMargin(workArea, 8, result);
    }
}
