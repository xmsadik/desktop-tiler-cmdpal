using System;
using System.Threading;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>
/// Coalesces a burst of rapid <see cref="Signal"/> calls into a single delayed invocation of the
/// callback - e.g. opening one app raises several show/cloak WinEvents, but subscribers only
/// need to be told once per burst. Built on <see cref="TimeProvider"/> (rather than a raw
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
    private readonly bool _extendOnSignal;
    private bool _armed;
    private bool _disposed;

    public Debouncer(TimeSpan delay, Action onFire, bool extendOnSignal = true)
        : this(delay, onFire, TimeProvider.System, extendOnSignal)
    {
    }

    /// <param name="extendOnSignal">True (the default): every signal pushes the due time back
    /// out, so a burst fires once, after it ends - but a source that never goes quiet never fires.
    /// False: the first signal sets the due time and later ones don't move it, so a steady stream
    /// of signals fires at most once per <paramref name="delay"/> (each firing covers every
    /// signal before it).</param>
    public Debouncer(TimeSpan delay, Action onFire, TimeProvider timeProvider, bool extendOnSignal = true)
    {
        ArgumentNullException.ThrowIfNull(onFire);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _delay = delay;
        _onFire = onFire;
        _extendOnSignal = extendOnSignal;
        _timer = timeProvider.CreateTimer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>(Re)starts the delay window: only the last call in a burst actually fires the
    /// callback, since each call pushes the due time back out. Without extendOnSignal, only starts
    /// it if it isn't already running.</summary>
    public void Signal()
    {
        lock (_gate)
        {
            if (_disposed || (_armed && !_extendOnSignal))
            {
                return;
            }

            _armed = true;
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

            _armed = false;
            _onFire();
        }
    }
}
