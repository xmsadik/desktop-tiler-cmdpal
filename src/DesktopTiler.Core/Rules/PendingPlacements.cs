using System;
using System.Collections.Generic;

namespace DesktopTiler.Core.Rules;

/// <summary>
/// "New windows of this app go to that desktop, for the next little while": what a workspace
/// leaves behind just before starting an app, so the window that app opens is moved to the
/// workspace's desktop. A placement covers every new window of the app until it expires (some
/// apps show a splash window before the main one), then drops off by itself. Times are
/// <see cref="Environment.TickCount64"/> milliseconds, passed in so tests can control them. Safe to
/// use from any thread.
/// </summary>
public sealed class PendingPlacements
{
    /// <summary>How long a placement waits for the app's windows.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly Dictionary<string, (Guid Desktop, long Expires)> _byApp = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds (or renews) the placement of <paramref name="app"/> (an executable file name).</summary>
    public void Add(string app, Guid desktopId, long nowTicks)
    {
        lock (_gate)
        {
            _byApp[app] = (desktopId, nowTicks + (long)Lifetime.TotalMilliseconds);
        }
    }

    /// <summary>Where new windows of <paramref name="app"/> go right now, if anywhere.</summary>
    public Guid? TargetFor(string app, long nowTicks)
    {
        lock (_gate)
        {
            Prune(nowTicks);
            return _byApp.TryGetValue(app, out var p) ? p.Desktop : null;
        }
    }

    /// <summary>Whether any placement is still waiting.</summary>
    public bool Any(long nowTicks)
    {
        lock (_gate)
        {
            Prune(nowTicks);
            return _byApp.Count > 0;
        }
    }

    private void Prune(long nowTicks)
    {
        List<string>? expired = null;
        foreach (var (app, p) in _byApp)
        {
            if (p.Expires <= nowTicks)
            {
                (expired ??= []).Add(app);
            }
        }

        if (expired is not null)
        {
            foreach (var app in expired)
            {
                _byApp.Remove(app);
            }
        }
    }
}
