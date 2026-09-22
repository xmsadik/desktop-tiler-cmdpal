using DesktopTiler.Core.Layouts;
using Xunit;

namespace DesktopTiler.Tests;

public class GridLayoutTests
{
    [Fact]
    public void Compute_ZeroWindows_ReturnsEmpty()
    {
        Assert.Empty(GridLayout.Compute(new Rect(0, 0, 1920, 1040), 0, 8));
    }

    [Fact]
    public void Compute_OneWindow_FillsWorkAreaMinusGapMargins()
    {
        var workArea = new Rect(0, 0, 1920, 1040);
        var result = GridLayout.Compute(workArea, 1, 8);

        var cell = Assert.Single(result);
        Assert.Equal(new Rect(8, 8, 1912, 1032), cell);
    }

    [Fact]
    public void Compute_TwoWindows_NoGap_IsOneRowOfTwoColumns()
    {
        var workArea = new Rect(0, 0, 200, 100);
        var result = GridLayout.Compute(workArea, 2, 0);

        Assert.Equal(2, result.Length);
        Assert.Equal(new Rect(0, 0, 100, 100), result[0]);
        Assert.Equal(new Rect(100, 0, 200, 100), result[1]);
    }

    [Fact]
    public void Compute_FourWindows_NoGap_IsFullTwoByTwoGrid()
    {
        var workArea = new Rect(0, 0, 90, 60);
        var result = GridLayout.Compute(workArea, 4, 0);

        Assert.Equal(4, result.Length);
        Assert.Equal(new Rect(0, 0, 45, 30), result[0]);
        Assert.Equal(new Rect(45, 0, 90, 30), result[1]);
        Assert.Equal(new Rect(0, 30, 45, 60), result[2]);
        Assert.Equal(new Rect(45, 30, 90, 60), result[3]);
    }

    [Fact]
    public void Compute_ThreeWindows_NoGap_LastRowStretchesItsSingleItemFullWidth()
    {
        var workArea = new Rect(0, 0, 90, 60);
        var result = GridLayout.Compute(workArea, 3, 0);

        Assert.Equal(3, result.Length);
        // 2x2 grid (cols=2, rows=2): full first row, stretched single-item last row.
        Assert.Equal(new Rect(0, 0, 45, 30), result[0]);
        Assert.Equal(new Rect(45, 0, 90, 30), result[1]);
        Assert.Equal(new Rect(0, 30, 90, 60), result[2]);
    }

    [Fact]
    public void Compute_FiveWindows_NoGap_LastRowStretchesItsTwoItems()
    {
        var workArea = new Rect(0, 0, 90, 60);
        var result = GridLayout.Compute(workArea, 5, 0);

        Assert.Equal(5, result.Length);
        // cols=3, rows=2: full 3-item first row, stretched 2-item last row (45 wide each,
        // not the 30 the first row's columns use).
        Assert.Equal(new Rect(0, 0, 30, 30), result[0]);
        Assert.Equal(new Rect(30, 0, 60, 30), result[1]);
        Assert.Equal(new Rect(60, 0, 90, 30), result[2]);
        Assert.Equal(new Rect(0, 30, 45, 60), result[3]);
        Assert.Equal(new Rect(45, 30, 90, 60), result[4]);
    }

    [Fact]
    public void Compute_SevenWindows_NoGap_LastRowStretchesItsSingleItem()
    {
        var workArea = new Rect(0, 0, 90, 90);
        var result = GridLayout.Compute(workArea, 7, 0);

        Assert.Equal(7, result.Length);
        // cols=3, rows=3: two full rows of 3, then a stretched single-item last row.
        Assert.Equal(new Rect(0, 0, 30, 30), result[0]);
        Assert.Equal(new Rect(30, 0, 60, 30), result[1]);
        Assert.Equal(new Rect(60, 0, 90, 30), result[2]);
        Assert.Equal(new Rect(0, 30, 30, 60), result[3]);
        Assert.Equal(new Rect(30, 30, 60, 60), result[4]);
        Assert.Equal(new Rect(60, 30, 90, 60), result[5]);
        Assert.Equal(new Rect(0, 60, 90, 90), result[6]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void Compute_WithGap_OddArea_TilesValidly(int gap)
    {
        var workArea = new Rect(0, 0, 1923, 1041);
        for (var n = 1; n <= 7; n++)
        {
            var result = GridLayout.Compute(workArea, n, gap);
            Assert.Equal(n, result.Length);
            LayoutAssert.IsValidTiling(workArea, gap, result);
            LayoutAssert.BoundingBoxMatchesMargin(workArea, gap, result);
        }
    }

    [Fact]
    public void Compute_NegativeGap_IsTreatedAsZero()
    {
        var workArea = new Rect(0, 0, 90, 60);
        var result = GridLayout.Compute(workArea, 4, -8);

        Assert.Equal(new Rect(0, 0, 45, 30), result[0]);
    }

    [Fact]
    public void Compute_NegativeOriginWorkArea_TilesValidly()
    {
        var workArea = new Rect(-1920, -200, 0, 880);
        var result = GridLayout.Compute(workArea, 5, 8);

        Assert.Equal(5, result.Length);
        LayoutAssert.IsValidTiling(workArea, 8, result);
        LayoutAssert.BoundingBoxMatchesMargin(workArea, 8, result);
    }
}
