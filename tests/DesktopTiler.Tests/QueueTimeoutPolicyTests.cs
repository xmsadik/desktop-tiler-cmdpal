using System;
using DesktopTiler.Core.VirtualDesktops;
using Xunit;

namespace DesktopTiler.Tests;

public class QueueTimeoutPolicyTests
{
    private static readonly QueueTimeoutPolicy Policy = new(TimeSpan.FromMilliseconds(1000));

    [Fact]
    public void ShouldFailFast_Idle_ReturnsFalse()
    {
        Assert.False(Policy.ShouldFailFast(currentActionStartTicks: 0, nowTicks: 1_000_000));
    }

    [Fact]
    public void ShouldFailFast_RunningWithinTimeout_ReturnsFalse()
    {
        Assert.False(Policy.ShouldFailFast(currentActionStartTicks: 1000, nowTicks: 1000 + 999));
    }

    [Fact]
    public void ShouldFailFast_RunningExactlyAtTimeout_ReturnsFalse()
    {
        // Strictly greater-than: exactly the timeout hasn't exceeded it yet.
        Assert.False(Policy.ShouldFailFast(currentActionStartTicks: 1000, nowTicks: 1000 + 1000));
    }

    [Fact]
    public void ShouldFailFast_RunningPastTimeout_ReturnsTrue()
    {
        Assert.True(Policy.ShouldFailFast(currentActionStartTicks: 1000, nowTicks: 1000 + 1001));
    }

    [Fact]
    public void DeadlineFrom_AddsTimeout()
    {
        Assert.Equal(2000, Policy.DeadlineFrom(nowTicks: 1000));
    }

    [Fact]
    public void HasExpired_BeforeDeadline_ReturnsFalse()
    {
        var deadline = Policy.DeadlineFrom(nowTicks: 1000);

        Assert.False(QueueTimeoutPolicy.HasExpired(deadline, nowTicks: 1999));
    }

    [Fact]
    public void HasExpired_ExactlyAtDeadline_ReturnsFalse()
    {
        var deadline = Policy.DeadlineFrom(nowTicks: 1000);

        Assert.False(QueueTimeoutPolicy.HasExpired(deadline, nowTicks: 2000));
    }

    [Fact]
    public void HasExpired_PastDeadline_ReturnsTrue()
    {
        var deadline = Policy.DeadlineFrom(nowTicks: 1000);

        Assert.True(QueueTimeoutPolicy.HasExpired(deadline, nowTicks: 2001));
    }
}
