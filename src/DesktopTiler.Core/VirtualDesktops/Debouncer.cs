using System;
using System.Threading;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>
/// Coalesces a burst of rapid <see cref="Signal"/> calls into a single delayed invocation of the
/// callback - e.g. Explorer writes several <c>VirtualDesktops</c> registry values per desktop
/// switch, and each write re-arms <see cref="RegistryDesktopReader"/>'s watch, but subscribers
/// only need to be told once per burst. Built on <see cref="TimeProvider"/> (rather than a raw
/// <see cref="Timer"/>) so the scheduling can be swapped out for a fake in unit tests.
/// </summary>
public sealed class Debouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Action _onFire;
    private readonly ITimer _timer;

    // ITimer.Dispose() (unlike System.Threading.Timer's WaitHandle overload) doesn't wait for an
    // in-flight callback, so without this a burst's callback could still fire after Dispose() has
    // returned. This lock makes Dispose() block until any such in-flight Fire() call completes -
    // and, as a side effect, also stops a post-Dispose Signal() from throwing
    // ObjectDisposedException by re-arming an already-disposed timer.
    private readonly object _gate = new();
    private bool _disposed;

    public Debouncer(TimeSpan delay, Action onFire)
        : this(delay, onFire, TimeProvider.System)
    {
    }

    public Debouncer(TimeSpan delay, Action onFire, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(onFire);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _delay = delay;
        _onFire = onFire;
        _timer = timeProvider.CreateTimer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>(re)starts the delay window. Only the last call in a burst actually fires the
    /// callback, since each call pushes the due time back out.</summary>
    public void Signal()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Dispose();
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _onFire();
        }
    }
}
