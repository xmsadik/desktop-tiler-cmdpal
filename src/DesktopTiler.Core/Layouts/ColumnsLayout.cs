namespace DesktopTiler.Core.Layouts;

/// <summary>
/// Pure geometry: divides a work area into <paramref name="n"/> equal-width columns, with
/// <paramref name="gap"/> pixels between adjacent columns and as the margin on all four edges of
/// the work area (left/right from the column split, top/bottom from a single-cell vertical
/// split - see <see cref="Split"/>). No Win32 calls, no window state - just arithmetic, so it is
/// fully unit-testable.
/// </summary>
public static class ColumnsLayout
{
    public static Rect[] Compute(Rect workArea, int n, int gap)
    {
        if (n <= 0)
        {
            return [];
        }

        var columns = Split.Span(workArea.Left, workArea.Right, n, gap);
        var (top, bottom) = Split.Span(workArea.Top, workArea.Bottom, 1, gap)[0];

        var result = new Rect[n];
        for (var i = 0; i < n; i++)
        {
            result[i] = new Rect(columns[i].Start, top, columns[i].End, bottom);
        }

        return result;
    }
}
