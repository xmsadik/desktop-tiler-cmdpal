using System.Linq;
using DesktopTiler.Core.Layouts;
using Xunit;

namespace DesktopTiler.Tests;

/// <summary>Shared geometry assertions reused by every layout's test class, so each one doesn't
/// re-derive "did this actually tile the area correctly": every cell stays inside the
/// margin-inset work area, no two cells overlap, and every cell has a positive width and
/// height.</summary>
internal static class LayoutAssert
{
    public static void IsValidTiling(Rect workArea, int gap, Rect[] cells)
    {
        AllInsideMargin(workArea, gap, cells);
        NoOverlaps(cells);
        AllPositiveSize(cells);
    }

    /// <summary>For gap-honouring layouts (everything except Monocle, which deliberately ignores
    /// the gap): the bounding box that encloses every cell must exactly reach the margin-inset
    /// edges of the work area - i.e. the cells collectively fill the whole available space, with
    /// no unclaimed strip left over anywhere along the outer edge.</summary>
    public static void BoundingBoxMatchesMargin(Rect workArea, int gap, Rect[] cells)
    {
        Assert.NotEmpty(cells);
        var safeGap = gap < 0 ? 0 : gap;

        Assert.Equal(workArea.Left + safeGap, cells.Min(c => c.Left));
        Assert.Equal(workArea.Top + safeGap, cells.Min(c => c.Top));
        Assert.Equal(workArea.Right - safeGap, cells.Max(c => c.Right));
        Assert.Equal(workArea.Bottom - safeGap, cells.Max(c => c.Bottom));
    }

    public static void AllInsideMargin(Rect workArea, int gap, Rect[] cells)
    {
        var safeGap = gap < 0 ? 0 : gap;
        foreach (var cell in cells)
        {
            Assert.True(cell.Left >= workArea.Left + safeGap, $"cell.Left {cell.Left} is inside the left margin");
            Assert.True(cell.Right <= workArea.Right - safeGap, $"cell.Right {cell.Right} is inside the right margin");
            Assert.True(cell.Top >= workArea.Top + safeGap, $"cell.Top {cell.Top} is inside the top margin");
            Assert.True(cell.Bottom <= workArea.Bottom - safeGap, $"cell.Bottom {cell.Bottom} is inside the bottom margin");
        }
    }

    public static void NoOverlaps(Rect[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            for (var j = i + 1; j < cells.Length; j++)
            {
                Assert.False(Overlaps(cells[i], cells[j]), $"cells[{i}] {cells[i]} overlaps cells[{j}] {cells[j]}");
            }
        }
    }

    public static void AllPositiveSize(Rect[] cells)
    {
        foreach (var cell in cells)
        {
            Assert.True(cell.Width > 0, $"cell {cell} has non-positive width");
            Assert.True(cell.Height > 0, $"cell {cell} has non-positive height");
        }
    }

    private static bool Overlaps(Rect a, Rect b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;
}
