namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>
/// The pure rules behind creating, removing, reordering and renaming desktops, kept apart from
/// the COM calls so they can be unit tested. Indexes are 0-based display positions.
/// </summary>
public static class DesktopManagementRules
{
    /// <summary>Longest desktop name accepted; a sanity cap, far beyond what fits on a Dock
    /// button.</summary>
    public const int MaxNameLength = 256;

    /// <summary>The desktop whose windows take over when the one at <paramref name="index"/> is
    /// removed: the one to its left, or the second one when the first is removed (the same
    /// choice Task View makes). Null when it is the only desktop, which can't be removed.</summary>
    public static int? FallbackIndex(int count, int index)
    {
        if (count <= 1 || index < 0 || index >= count)
        {
            return null;
        }

        return index > 0 ? index - 1 : 1;
    }

    /// <summary>Where the desktop at <paramref name="index"/> ends up when moved
    /// <paramref name="step"/> places (-1 left, +1 right); null if that's past either end.</summary>
    public static int? MoveTargetIndex(int count, int index, int step)
    {
        if (index < 0 || index >= count)
        {
            return null;
        }

        var target = index + step;
        return target >= 0 && target < count && target != index ? target : null;
    }

    /// <summary>The name to store for what the user typed: trimmed, cut to
    /// <see cref="MaxNameLength"/>, and empty (meaning "no name, show the default") for blank
    /// input.</summary>
    public static string NormalizeName(string? input)
    {
        var name = input?.Trim() ?? string.Empty;
        return name.Length > MaxNameLength ? name[..MaxNameLength].TrimEnd() : name;
    }
}
