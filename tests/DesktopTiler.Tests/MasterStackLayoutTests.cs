using DesktopTiler.Core.Layouts;
using Xunit;

namespace DesktopTiler.Tests;

public class MasterStackLayoutTests
{
    [Fact]
    public void Compute_ZeroWindows_ReturnsEmpty()
    {
        Assert.Empty(MasterStackLayout.Compute(new Rect(0, 0, 1920, 1040), 0, 8, 0.5));
    }

    [Fact]
    public void Compute_OneWindow_FillsWorkAreaMinusGapMargins()
    {
        var workArea = new Rect(0, 0, 1920, 1040);
        var result = MasterStackLayout.Compute(workArea, 1, 8, 0.55);

        var cell = Assert.Single(result);
        Assert.Equal(new Rect(8, 8, 1912, 1032), cell);
    }

    [Fact]
    public void Compute_TwoWindows_NoGap_SplitsByRatio()
    {
        var workArea = new Rect(0, 0, 200, 100);
        var result = MasterStackLayout.Compute(workArea, 2, 0, 0.5);

        Assert.Equal(2, result.Length);
        Assert.Equal(new Rect(0, 0, 100, 100), result[0]);
        Assert.Equal(new Rect(100, 0, 200, 100), result[1]);
    }

    [Fact]
    public void Compute_ThreeWindows_NoGap_StackHasTwoEqualRows()
    {
        var workArea = new Rect(0, 0, 200, 100);
        var result = MasterStackLayout.Compute(workArea, 3, 0, 0.5);

        Assert.Equal(3, result.Length);
        Assert.Equal(new Rect(0, 0, 100, 100), result[0]);
        Assert.Equal(new Rect(100, 0, 200, 50), result[1]);
        Assert.Equal(new Rect(100, 50, 200, 100), result[2]);
    }

    [Fact]
    public void Compute_WithGap_MasterAndStackAreSeparatedByExactlyOneGap()
    {
        var workArea = new Rect(0, 0, 216, 100);
        var result = MasterStackLayout.Compute(workArea, 3, 8, 0.5);

        Assert.Equal(8, result[0].Left - workArea.Left);
        Assert.Equal(8, result[1].Left - result[0].Right);
        Assert.Equal(result[1].Left, result[2].Left);
        Assert.Equal(workArea.Right - 8, result[1].Right);
        Assert.Equal(8, result[2].Top - result[1].Bottom);
        LayoutAssert.IsValidTiling(workArea, 8, result);
        LayoutAssert.BoundingBoxMatchesMargin(workArea, 8, result);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Compute_ManyWindows_OddArea_TilesValidlyWithGap(int n)
    {
        var workArea = new Rect(0, 0, 1923, 1041);
        var result = MasterStackLayout.Compute(workArea, n, 8, 0.55);

        Assert.Equal(n, result.Length);
        LayoutAssert.IsValidTiling(workArea, 8, result);
        LayoutAssert.BoundingBoxMatchesMargin(workArea, 8, result);
    }

    [Fact]
    public void Compute_NegativeOriginWorkArea_TilesValidly()
    {
        var workArea = new Rect(-1920, -200, 0, 880);
        var result = MasterStackLayout.Compute(workArea, 4, 8, 0.55);

        Assert.Equal(4, result.Length);
        LayoutAssert.IsValidTiling(workArea, 8, result);
        LayoutAssert.BoundingBoxMatchesMargin(workArea, 8, result);
    }

    [Fact]
    public void Compute_RatioBelowMinimum_IsClampedTo0Point1()
    {
        var workArea = new Rect(0, 0, 200, 100);
        var clamped = MasterStackLayout.Compute(workArea, 2, 0, 0.1);
        var tooLow = MasterStackLayout.Compute(workArea, 2, 0, -5.0);

        Assert.Equal(clamped, tooLow);
    }

    [Fact]
    public void Compute_RatioAboveMaximum_IsClampedTo0Point9()
    {
        var workArea = new Rect(0, 0, 200, 100);
        var clamped = MasterStackLayout.Compute(workArea, 2, 0, 0.9);
        var tooHigh = MasterStackLayout.Compute(workArea, 2, 0, 5.0);

        Assert.Equal(clamped, tooHigh);
    }

    [Fact]
    public void Compute_NegativeGap_IsTreatedAsZero()
    {
        var workArea = new Rect(0, 0, 200, 100);
        var result = MasterStackLayout.Compute(workArea, 2, -8, 0.5);

        Assert.Equal(workArea.Left, result[0].Left);
        Assert.Equal(workArea.Right, result[1].Right);
    }
}
