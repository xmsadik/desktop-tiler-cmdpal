using System;
using System.Collections.Generic;
using System.Text;

namespace DesktopTiler.Core.Windows;

/// <summary>
/// Pure helpers for the Dock band: which app windows are on which desktop (for the occupancy
/// icons and the hover tooltip), whether that changed, and the tooltip text. No Win32 calls.
/// </summary>
public static class DesktopWindowGroups
{
    /// <summary>The most window titles listed in a desktop button's tooltip; the rest are
    /// summarised as "+N more".</summary>
    public const int MaxTooltipTitles = 8;

    /// <summary>Longer window titles are cut to this many characters (plus "…") so one browser
    /// tab doesn't fill the tooltip.</summary>
    public const int MaxTooltipTitleLength = 60;

    /// <summary>Groups <paramref name="handles"/> by their desktop id (same index in
    /// <paramref name="desktopIds"/>), keeping their order (Z-order, front first). Windows whose id
    /// couldn't be read (<see cref="Guid.Empty"/>) are skipped, as for the occupancy icons.</summary>
    public static Dictionary<Guid, List<nint>> Group(IReadOnlyList<nint> handles, IReadOnlyList<Guid> desktopIds)
    {
        ArgumentNullException.ThrowIfNull(handles);
        ArgumentNullException.ThrowIfNull(desktopIds);

        var groups = new Dictionary<Guid, List<nint>>();
        for (var i = 0; i < handles.Count && i < desktopIds.Count; i++)
        {
            var id = desktopIds[i];
            if (id == Guid.Empty)
            {
                continue;
            }

            if (!groups.TryGetValue(id, out var list))
            {
                list = [];
                groups[id] = list;
            }

            list.Add(handles[i]);
        }

        return groups;
    }

    /// <summary>Whether both groupings put the same set of windows on each desktop. Order and
    /// titles are ignored on purpose: the band only needs re-rendering when windows appear,
    /// disappear or move between desktops, not on every Z-order or title change.</summary>
    public static bool SameMembership(IReadOnlyDictionary<Guid, List<nint>> a, IReadOnlyDictionary<Guid, List<nint>> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var (id, windows) in a)
        {
            if (!b.TryGetValue(id, out var other) || !new HashSet<nint>(windows).SetEquals(other))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The subtitle that carries a desktop button's tooltip. The Dock builds the tooltip as
    /// "title, newline, subtitle" and has no tooltip of its own, so the window titles go on extra
    /// subtitle lines after the desktop name (the first line, the only one the label shows).
    /// Empty titles are skipped; past <see cref="MaxTooltipTitles"/> the rest become "+N more".
    /// </summary>
    public static string TooltipSubtitle(string? desktopName, IReadOnlyList<string> windowTitles)
    {
        ArgumentNullException.ThrowIfNull(windowTitles);

        var sb = new StringBuilder(desktopName ?? string.Empty);
        var listed = 0;
        var nonEmpty = 0;
        foreach (var title in windowTitles)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            nonEmpty++;
            if (listed < MaxTooltipTitles)
            {
                var t = title.Trim();
                if (t.Length > MaxTooltipTitleLength)
                {
                    t = string.Concat(t.AsSpan(0, MaxTooltipTitleLength).TrimEnd(), "…");
                }

                sb.Append('\n').Append("• ").Append(t);
                listed++;
            }
        }

        if (nonEmpty > listed)
        {
            sb.Append('\n').Append('+').Append(nonEmpty - listed).Append(" more");
        }

        return sb.ToString();
    }
}
