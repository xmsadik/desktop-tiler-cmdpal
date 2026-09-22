using System;

namespace DesktopTiler.Core.Layouts;

/// <summary>
/// Classic master + stack layout: one master window (index 0) takes a fixed-ratio column on the
/// left, and the rest are stacked vertically in equal-height rows on the right. n=1 collapses to
/// a single cell filling the (margin-inset) work area - there's no stack column to make room for.
/// </summary>
public static class MasterStackLayout
{
    public static Rect[] Compute(Rect workArea, int n, int gap, double masterRatio)
    {
        if (n <= 0)
        {
            return [];
        }

        masterRatio = ClampRatio(masterRatio);
        var (top, bottom) = Split.Span(workArea.Top, workArea.Bottom, 1, gap)[0];

        if (n == 1)
        {
            var (left, right) = Split.Span(workArea.Left, workArea.Right, 1, gap)[0];
            return [new Rect(left, top, right, bottom)];
        }

        // usableWidth = full width minus the two outer margins minus the one gap between the
        // master column and the stack column (3 gaps total).
        var (safeGap, usableWidth) = Split.Normalize(workArea.Width, gapCount: 3, gap, minUsable: 2);
        var masterWidth = (int)Math.Round(usableWidth * masterRatio, MidpointRounding.AwayFromZero);

        // Not Math.Clamp: on a degenerate work area (e.g. Rect(0,0,0,0) from a monitor query
        // that failed) usableWidth can be <= 1, making the upper bound less than the lower bound
        // 1, which Math.Clamp throws on. Math.Max(Math.Min(...)) just settles on 1 instead.
        masterWidth = Math.Max(1, Math.Min(masterWidth, usableWidth - 1));

        var masterLeft = workArea.Left + safeGap;
        var masterRight = masterLeft + masterWidth;
        var stackLeft = masterRight + safeGap;
        var stackRight = workArea.Right - safeGap;

        var result = new Rect[n];
        result[0] = new Rect(masterLeft, top, masterRight, bottom);

        var rows = Split.Span(top, bottom, n - 1, gap);
        for (var i = 0; i < rows.Length; i++)
        {
            result[i + 1] = new Rect(stackLeft, rows[i].Start, stackRight, rows[i].End);
        }

        return result;
    }

    /// <summary>Shared with <see cref="CenterMasterLayout"/>, which uses the same ratio and the
    /// same defensive clamp.</summary>
    internal static double ClampRatio(double ratio) => Math.Clamp(ratio, 0.1, 0.9);
}
