using System;

namespace DesktopTiler.Core.Layouts;

/// <summary>
/// Every window gets the full work area, with no gaps - the "one window at a time" layout.
/// <c>Tiler</c> actually maximizes windows for Monocle instead of moving them to these rects (see
/// <c>WindowMover.MaximizeAll</c>), but Compute still returns n copies of the work area so
/// <see cref="LayoutEngine"/> dispatch and tests stay uniform across all five layouts.
/// </summary>
public static class MonocleLayout
{
    public static Rect[] Compute(Rect workArea, int n)
    {
        if (n <= 0)
        {
            return [];
        }

        var result = new Rect[n];
        Array.Fill(result, workArea);
        return result;
    }
}
