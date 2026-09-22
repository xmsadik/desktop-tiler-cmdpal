using System;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>
/// Pure fail-fast/deadline policy behind <see cref="VdComClient"/>'s per-call timeout, extracted so
/// it can be unit tested against an injected tick count instead of real wall-clock timing and
/// threads - same rationale as <see cref="ComRetry"/> and <see cref="VdConnectionState"/>.
///
/// Two independent checks against the same timeout, both needed because a call queued behind a
/// wedged STA thread (one stuck in a hung COM call) would otherwise either wait indefinitely or -
/// once the hang clears - run late against stale state:
///
/// 1. <see cref="ShouldFailFast"/> - before a new call is even queued: has the action currently
///    executing on the STA thread already run longer than the timeout? If so, the thread is
///    presumed wedged and the new call should fail immediately rather than join the queue at all.
/// 2. <see cref="HasExpired"/> - once a queued call is finally dequeued: has more than the timeout
///    elapsed since it was queued (see <see cref="DeadlineFrom"/>)? If so, it should be faulted,
///    not run, so a backlog built up during an outage doesn't all execute later regardless.
///
/// All ticks are the same clock/units the caller uses throughout one policy's lifetime (e.g.
/// <see cref="Environment.TickCount64"/> in <see cref="VdComClient"/>) - the policy itself never
/// reads the clock, which is what makes it pure.
/// </summary>
public readonly struct QueueTimeoutPolicy
{
    private readonly long _timeoutMs;

    public QueueTimeoutPolicy(TimeSpan timeout)
    {
        _timeoutMs = (long)timeout.TotalMilliseconds;
    }

    /// <param name="currentActionStartTicks">0 if idle, else the tick count when the
    /// currently-executing action started.</param>
    /// <param name="nowTicks">The current tick count.</param>
    public bool ShouldFailFast(long currentActionStartTicks, long nowTicks) =>
        currentActionStartTicks != 0 && nowTicks - currentActionStartTicks > _timeoutMs;

    /// <summary>The deadline to record for a call queued at <paramref name="nowTicks"/>, to later
    /// pass to <see cref="HasExpired"/>.</summary>
    public long DeadlineFrom(long nowTicks) => nowTicks + _timeoutMs;

    /// <summary>Whether a queued call with the given <paramref name="deadlineTicks"/> (from
    /// <see cref="DeadlineFrom"/>) has expired by <paramref name="nowTicks"/>. Static - unlike
    /// <see cref="ShouldFailFast"/>/<see cref="DeadlineFrom"/>, comparing two already-computed tick
    /// values needs no timeout of its own.</summary>
    public static bool HasExpired(long deadlineTicks, long nowTicks) => nowTicks > deadlineTicks;
}
