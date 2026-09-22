namespace DesktopTiler.Core.Layouts;

/// <summary>
/// Shared 1-D gap arithmetic, reused by every layout instead of each one re-deriving it: split
/// [start, end) into <c>count</c> adjacent cells, with <c>gap</c> pixels of margin at both ends
/// of the span and between adjacent cells. A negative gap is treated as zero, and if the span is
/// too small to fit the requested gaps the whole split falls back to gap 0 (using the full span
/// as usable space) rather than producing negative-width cells.
/// </summary>
internal static class Split
{
    /// <summary>Splits [start, end) into <paramref name="count"/> adjacent (Start, End) pairs.
    /// A margin-only split (e.g. the top/bottom margin of a row of columns) is just this with
    /// <paramref name="count"/> == 1.</summary>
    public static (int Start, int End)[] Span(int start, int end, int count, int gap)
    {
        if (count <= 0)
        {
            return [];
        }

        var (safeGap, usable) = Normalize(end - start, count + 1, gap, count);

        var size = usable / count;
        var remainder = usable - (size * count);

        var result = new (int Start, int End)[count];
        var cursor = start + safeGap;
        for (var i = 0; i < count; i++)
        {
            // Give the last cell any leftover pixels from integer division so the cells exactly
            // reach the trailing margin instead of leaving a sliver before it.
            var cellSize = size + (i == count - 1 ? remainder : 0);
            result[i] = (cursor, cursor + cellSize);
            cursor += cellSize + safeGap;
        }

        return result;
    }

    /// <summary>
    /// Clamps a negative gap to zero and, if <paramref name="length"/> can't fit
    /// <paramref name="gapCount"/> gaps while leaving at least <paramref name="minUsable"/> pixels
    /// of usable space, falls back to gap 0 (the full length becomes usable). Shared by
    /// <see cref="Span"/>'s equal-division case and by the ratio-based master/center-master width
    /// splits, which need the same normalization but divide the usable space unevenly.
    /// </summary>
    public static (int Gap, int Usable) Normalize(int length, int gapCount, int gap, int minUsable)
    {
        if (gap < 0)
        {
            gap = 0;
        }

        var usable = length - (gap * gapCount);
        if (usable < minUsable)
        {
            return (0, length);
        }

        return (gap, usable);
    }
}
