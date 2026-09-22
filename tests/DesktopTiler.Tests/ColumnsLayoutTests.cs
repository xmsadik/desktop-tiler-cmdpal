using DesktopTiler.Core.Layouts;
using Xunit;

namespace DesktopTiler.Tests;

public class ColumnsLayoutTests
{
    [Fact]
    public void Compute_ZeroColumns_ReturnsEmpty()
    {
        var result = ColumnsLayout.Compute(new Rect(0, 0, 1920, 1040), 0, 8);
        Assert.Empty(result);
    }

    [Fact]
    public void Compute_NegativeColumns_ReturnsEmpty()
    {
        var result = ColumnsLayout.Compute(new Rect(0, 0, 1920, 1040), -1, 8);
        Assert.Empty(result);
    }

    [Fact]
    public void Compute_OneColumn_FillsWorkAreaMinusGapMargins()
    {
        var workArea = new Rect(0, 0, 1920, 1040);
        var result = ColumnsLayout.Compute(workArea, 1, 8);

        var column = Assert.Single(result);
        Assert.Equal(8, column.Left);
        Assert.Equal(1912, column.Right);
        Assert.Equal(8, column.Top);
        Assert.Equal(1032, column.Bottom);
    }

    [Fact]
    public void Compute_TwoColumns_AreEqualWidthAndAdjacentWithGap()
    {
        var workArea = new Rect(0, 0, 1920, 1040);
        var result = ColumnsLayout.Compute(workArea, 2, 8);

        Assert.Equal(2, result.Length);
        Assert.Equal(result[0].Width, result[1].Width);
        Assert.Equal(8, result[1].Left - result[0].Right);
        Assert.Equal(8, result[0].Left - workArea.Left);
        Assert.Equal(workArea.Right - 8, result[1].Right);
        foreach (var column in result)
        {
            Assert.Equal(workArea.Top + 8, column.Top);
            Assert.Equal(workArea.Bottom - 8, column.Bottom);
        }
    }

    [Fact]
    public void Compute_ThreeColumns_TileExactlyToTheEdges()
    {
        var workArea = new Rect(0, 0, 1923, 1040);
        var result = ColumnsLayout.Compute(workArea, 3, 8);

        Assert.Equal(3, result.Length);
        Assert.Equal(workArea.Left + 8, result[0].Left);
        Assert.Equal(workArea.Right - 8, result[2].Right);
        for (var i = 0; i < result.Length - 1; i++)
        {
            Assert.Equal(8, result[i + 1].Left - result[i].Right);
        }
    }

    [Fact]
    public void Compute_ZeroGap_StillTilesTheFullWidth()
    {
        var workArea = new Rect(0, 0, 100, 50);
        var result = ColumnsLayout.Compute(workArea, 4, 0);

        Assert.Equal(4, result.Length);
        Assert.Equal(workArea.Left, result[0].Left);
        Assert.Equal(workArea.Right, result[3].Right);
        for (var i = 0; i < result.Length - 1; i++)
        {
            Assert.Equal(result[i].Right, result[i + 1].Left);
        }
    }

    [Fact]
    public void Compute_NegativeGap_IsTreatedAsZero()
    {
        var workArea = new Rect(0, 0, 100, 50);
        var result = ColumnsLayout.Compute(workArea, 2, -8);

        Assert.Equal(workArea.Left, result[0].Left);
        Assert.Equal(workArea.Right, result[1].Right);
    }

    [Fact]
    public void Compute_NegativeOriginWorkArea_TilesValidly()
    {
        var workArea = new Rect(-1920, -200, 0, 880);
        var result = ColumnsLayout.Compute(workArea, 3, 8);

        Assert.Equal(3, result.Length);
        LayoutAssert.IsValidTiling(workArea, 8, result);
        LayoutAssert.BoundingBoxMatchesMargin(workArea, 8, result);
    }
}
