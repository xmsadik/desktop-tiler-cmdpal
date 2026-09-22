using System;
using System.Collections.Generic;
using System.Threading;
using DesktopTiler.Core.VirtualDesktops;
using Xunit;

namespace DesktopTiler.Tests;

public class DebouncerTests
{
    /// <summary>A <see cref="TimeProvider"/> whose timer never fires on its own - the test fires
    /// it explicitly via <see cref="Fire"/> - so the debounce delay itself needs no real waiting.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        public List<TimeSpan> ChangeCalls { get; } = [];

        public int DisposeCount { get; private set; }

        private TimerCallback? _callback;
        private object? _state;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            _state = state;
            return new ManualTimer(this);
        }

        public void Fire() => _callback?.Invoke(_state);

        private sealed class ManualTimer(ManualTimeProvider owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                owner.ChangeCalls.Add(dueTime);
                return true;
            }

            public void Dispose() => owner.DisposeCount++;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    [Fact]
    public void Signal_PushesTheDueTimeOutByDelay()
    {
        var provider = new ManualTimeProvider();
        var delay = TimeSpan.FromMilliseconds(75);
        using var debouncer = new Debouncer(delay, () => { }, provider);

        debouncer.Signal();

        var dueTime = Assert.Single(provider.ChangeCalls);
        Assert.Equal(delay, dueTime);
    }

    [Fact]
    public void Signal_CalledRepeatedly_ResetsTheDueTimeEachTime()
    {
        var provider = new ManualTimeProvider();
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(75), () => { }, provider);

        debouncer.Signal();
        debouncer.Signal();
        debouncer.Signal();

        // Each Signal() call resets the underlying timer's due time - a burst of calls only ever
        // has its LAST Change() honored by the real timer, so exactly one callback eventually
        // fires no matter how many times Signal() was called during the burst.
        Assert.Equal(3, provider.ChangeCalls.Count);
    }

    [Fact]
    public void Fire_AfterOneOrMoreSignals_InvokesCallbackExactlyOnce()
    {
        var provider = new ManualTimeProvider();
        var fireCount = 0;
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(75), () => fireCount++, provider);

        debouncer.Signal();
        debouncer.Signal();
        provider.Fire();

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Dispose_DisposesTheUnderlyingTimer()
    {
        var provider = new ManualTimeProvider();
        var debouncer = new Debouncer(TimeSpan.FromMilliseconds(75), () => { }, provider);

        debouncer.Dispose();

        Assert.Equal(1, provider.DisposeCount);
    }

    [Fact]
    public void Fire_AfterDispose_DoesNotInvokeCallback()
    {
        // Simulates the race Timer/ITimer.Dispose() doesn't protect against: a callback already
        // queued (or, here, fired manually) by the time Dispose() runs must not still invoke the
        // debounced action.
        var provider = new ManualTimeProvider();
        var fireCount = 0;
        var debouncer = new Debouncer(TimeSpan.FromMilliseconds(75), () => fireCount++, provider);
        debouncer.Signal();

        debouncer.Dispose();
        provider.Fire();

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void Signal_AfterDispose_DoesNotThrowOrReconfigureTheDisposedTimer()
    {
        var provider = new ManualTimeProvider();
        var debouncer = new Debouncer(TimeSpan.FromMilliseconds(75), () => { }, provider);
        debouncer.Signal();
        var changeCallsBeforeDispose = provider.ChangeCalls.Count;

        debouncer.Dispose();
        debouncer.Signal();

        Assert.Equal(changeCallsBeforeDispose, provider.ChangeCalls.Count);
    }
}
