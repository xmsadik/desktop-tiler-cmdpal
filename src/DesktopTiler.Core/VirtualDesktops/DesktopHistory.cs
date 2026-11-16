using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>
/// Remembers the order desktops were last used in, most recent (the current one) first - like
/// Alt+Tab's order for windows. Feeds "Last used desktop" (flip between the two most recent
/// desktops) and the "Recent desktops" page. Fed every observed current desktop id, from
/// whichever thread notices a change; repeated or empty ids are ignored. Thread-safe.
/// </summary>
public sealed class DesktopHistory
{
    /// <summary>How many desktops are remembered; far more than anyone keeps open.</summary>
    public const int Capacity = 32;

    private readonly object _gate = new();
    private readonly List<Guid> _recentFirst = [];

    /// <summary>The desktop active before the current one, or null if no switch has been seen
    /// yet.</summary>
    public Guid? Previous
    {
        get
        {
            lock (_gate)
            {
                return _recentFirst.Count > 1 ? _recentFirst[1] : null;
            }
        }
    }

    /// <summary>The most recently used desktop other than the current one that is still among
    /// <paramref name="existing"/> (so a deleted desktop is skipped), or null if there's none.</summary>
    public Guid? PreviousAmong(IEnumerable<Guid> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var alive = existing.ToHashSet();
        lock (_gate)
        {
            foreach (var id in _recentFirst.Skip(1))
            {
                if (alive.Contains(id))
                {
                    return id;
                }
            }
        }

        return null;
    }

    /// <summary><paramref name="desktopsInOrder"/> reordered most recently used first; desktops
    /// never seen as current keep their display order, after the others.</summary>
    public IReadOnlyList<Guid> Order(IReadOnlyList<Guid> desktopsInOrder)
    {
        ArgumentNullException.ThrowIfNull(desktopsInOrder);
        var present = desktopsInOrder.ToHashSet();
        List<Guid> seen;
        lock (_gate)
        {
            seen = [.. _recentFirst.Where(present.Contains)];
        }

        var seenSet = seen.ToHashSet();
        return [.. seen, .. desktopsInOrder.Where(d => !seenSet.Contains(d))];
    }

    /// <summary>Records <paramref name="currentId"/> as the active desktop, moving it to the
    /// front of the order.</summary>
    public void Observe(Guid currentId)
    {
        if (currentId == Guid.Empty)
        {
            return;
        }

        lock (_gate)
        {
            if (_recentFirst.Count > 0 && _recentFirst[0] == currentId)
            {
                return;
            }

            _recentFirst.Remove(currentId);
            _recentFirst.Insert(0, currentId);
            if (_recentFirst.Count > Capacity)
            {
                _recentFirst.RemoveAt(_recentFirst.Count - 1);
            }
        }
    }
}
