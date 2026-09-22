using System;
using System.Collections.Generic;

namespace DesktopTiler.Core.Layouts;

/// <summary>
/// Master window centered horizontally, with the remaining windows split into left/right stack
/// columns, assigned alternately starting with RIGHT (index 1 -&gt; right, index 2 -&gt; left,
/// index 3 -&gt; right, ...) so the two columns fill up evenly. n &lt;= 2 has no side columns to
/// speak of, so it collapses to the same shape as <see cref="MasterStackLayout"/> (n=1: a single
/// cell; n=2: master + one stack window) - there is no reason to reserve an empty second side
/// column just to hold one stack window.
/// </summary>
public static class CenterMasterLayout
{
    public static Rect[] Compute(Rect workArea, int n, int gap, double masterRatio)
    {
        if (n <= 0)
        {
            return [];
        }

        if (n <= 2)
        {
            return MasterStackLayout.Compute(workArea, n, gap, masterRatio);
        }

        masterRatio = MasterStackLayout.ClampRatio(masterRatio);
        var (top, bottom) = Split.Span(workArea.Top, workArea.Bottom, 1, gap)[0];

        // usableWidth = full width minus the two outer margins minus the two inner gaps
        // (left-stack-to-master and master-to-right-stack - 4 gaps total).
        var (safeGap, usableWidth) = Split.Normalize(workArea.Width, gapCount: 4, gap, minUsable: 3);
        var masterWidth = (int)Math.Round(usableWidth * masterRatio, MidpointRounding.AwayFromZero);

        // Not Math.Clamp: on a degenerate/tiny work area usableWidth can be <= 2, making the
        // upper bound less than the lower bound 1, which Math.Clamp throws on. Math.Max(Math.Min
        // (...)) just settles on 1 instead of throwing.
        masterWidth = Math.Max(1, Math.Min(masterWidth, usableWidth - 2));

        // The remaining width splits equally into the left/right columns; any odd pixel goes to
        // the right column, which also gets the first stack window (see the alternation below).
        var sideWidth = usableWidth - masterWidth;
        var leftWidth = sideWidth / 2;
        var rightWidth = sideWidth - leftWidth;

        var leftLeft = workArea.Left + safeGap;
        var leftRight = leftLeft + leftWidth;
        var masterLeft = leftRight + safeGap;
        var masterRight = masterLeft + masterWidth;
        var rightLeft = masterRight + safeGap;
        var rightRight = rightLeft + rightWidth;

        var result = new Rect[n];
        result[0] = new Rect(masterLeft, top, masterRight, bottom);

        var leftIndices = new List<int>();
        var rightIndices = new List<int>();
        for (var i = 1; i < n; i++)
        {
            // RIGHT, LEFT, RIGHT, LEFT... starting with RIGHT for the first stack window (index 1).
            if ((i - 1) % 2 == 0)
            {
                rightIndices.Add(i);
            }
            else
            {
                leftIndices.Add(i);
            }
        }

        AssignColumn(result, rightIndices, top, bottom, rightLeft, rightRight, gap);
        AssignColumn(result, leftIndices, top, bottom, leftLeft, leftRight, gap);

        return result;
    }

    private static void AssignColumn(Rect[] result, List<int> indices, int top, int bottom, int left, int right, int gap)
    {
        var rows = Split.Span(top, bottom, indices.Count, gap);
        for (var i = 0; i < indices.Count; i++)
        {
            result[indices[i]] = new Rect(left, rows[i].Start, right, rows[i].End);
        }
    }
}
