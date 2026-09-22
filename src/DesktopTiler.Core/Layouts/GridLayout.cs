using System;

namespace DesktopTiler.Core.Layouts;

/// <summary>
/// Roughly-square grid: cols = ceil(sqrt(n)), rows = ceil(n / cols), filled row-major. The last
/// row, when it has fewer than <c>cols</c> windows, re-splits the full width among just its own
/// windows instead of leaving empty holes where the missing columns would have been. Row heights
/// are equal across every row.
/// </summary>
public static class GridLayout
{
    public static Rect[] Compute(Rect workArea, int n, int gap)
    {
        if (n <= 0)
        {
            return [];
        }

        var cols = (int)Math.Ceiling(Math.Sqrt(n));
        var rows = (int)Math.Ceiling(n / (double)cols);

        var rowSpans = Split.Span(workArea.Top, workArea.Bottom, rows, gap);

        var result = new Rect[n];
        var index = 0;
        for (var r = 0; r < rows; r++)
        {
            var itemsInRow = Math.Min(cols, n - index);
            var colSpans = Split.Span(workArea.Left, workArea.Right, itemsInRow, gap);
            var (top, bottom) = rowSpans[r];
            for (var c = 0; c < itemsInRow; c++)
            {
                result[index] = new Rect(colSpans[c].Start, top, colSpans[c].End, bottom);
                index++;
            }
        }

        return result;
    }
}
