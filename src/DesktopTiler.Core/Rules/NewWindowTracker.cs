using System.Collections.Generic;

namespace DesktopTiler.Core.Rules;

/// <summary>
/// Tells which windows are new since the last look. The window watcher only says "something
/// changed", so each look passes the full current window list and gets back the handles that
/// weren't in the previous one. The first look (or the first after <see cref="Reset"/>) only
/// records what is there and reports nothing, so windows that were already open are never
/// treated as new. Not thread-safe; the caller serialises calls.
/// </summary>
public sealed class NewWindowTracker
{
    private HashSet<nint>? _known;

    /// <summary>Records <paramref name="current"/> as the open windows and returns those of them
    /// that weren't open last time (empty on the first call).</summary>
    public IReadOnlyList<nint> Update(IEnumerable<nint> current)
    {
        var now = new HashSet<nint>(current);
        var added = new List<nint>();
        if (_known is not null)
        {
            foreach (var h in now)
            {
                if (!_known.Contains(h))
                {
                    added.Add(h);
                }
            }
        }

        _known = now;
        return added;
    }

    /// <summary>Forgets the recorded windows, so the next <see cref="Update"/> starts afresh.</summary>
    public void Reset() => _known = null;
}
