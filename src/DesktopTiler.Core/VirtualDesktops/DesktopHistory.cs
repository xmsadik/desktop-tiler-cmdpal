using System;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>
/// Remembers the desktop that was active before the current one, for "Last used desktop"
/// (flip between the two most recent desktops, like Alt+Tab for windows). Fed every observed
/// current desktop id, from whichever thread notices a change; repeated or empty ids are
/// ignored. Thread-safe.
/// </summary>
public sealed class DesktopHistory
{
    private readonly object _gate = new();
    private Guid _current;
    private Guid _previous;

    /// <summary>The desktop active before the current one, or null if no switch has been seen
    /// yet.</summary>
    public Guid? Previous
    {
        get
        {
            lock (_gate)
            {
                return _previous == Guid.Empty ? null : _previous;
            }
        }
    }

    /// <summary>Records <paramref name="currentId"/> as the active desktop. A different id than
    /// last time makes the old one <see cref="Previous"/>.</summary>
    public void Observe(Guid currentId)
    {
        if (currentId == Guid.Empty)
        {
            return;
        }

        lock (_gate)
        {
            if (currentId == _current)
            {
                return;
            }

            if (_current != Guid.Empty)
            {
                _previous = _current;
            }

            _current = currentId;
        }
    }
}
