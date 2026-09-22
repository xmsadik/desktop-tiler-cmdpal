using System;
using DesktopTiler.Core.VirtualDesktops;
using Xunit;

namespace DesktopTiler.Tests;

public class ComRetryTests
{
    private sealed class TransientException : Exception;

    private sealed class PermanentException : Exception;

    [Fact]
    public void Run_FirstCallSucceeds_ReturnsResultAndNeverReconnects()
    {
        var reconnectCount = 0;

        var result = ComRetry.Run(
            () => 42,
            () => reconnectCount++,
            _ => true);

        Assert.Equal(42, result);
        Assert.Equal(0, reconnectCount);
    }

    [Fact]
    public void Run_OneTransientFailureThenSuccess_ReconnectsOnceAndReturnsResult()
    {
        var attempt = 0;
        var reconnectCount = 0;

        var result = ComRetry.Run(
            () =>
            {
                attempt++;
                if (attempt == 1)
                {
                    throw new TransientException();
                }

                return "ok";
            },
            () => reconnectCount++,
            ex => ex is TransientException);

        Assert.Equal("ok", result);
        Assert.Equal(2, attempt);
        Assert.Equal(1, reconnectCount);
    }

    [Fact]
    public void Run_TwoTransientFailures_ReconnectsOnceThenThrows()
    {
        var attempt = 0;
        var reconnectCount = 0;

        Assert.Throws<TransientException>(() => ComRetry.Run<int>(
            () =>
            {
                attempt++;
                throw new TransientException();
            },
            () => reconnectCount++,
            ex => ex is TransientException));

        Assert.Equal(2, attempt);
        Assert.Equal(1, reconnectCount);
    }

    [Fact]
    public void Run_NonTransientError_ThrowsImmediatelyWithoutReconnect()
    {
        var attempt = 0;
        var reconnectCount = 0;

        Assert.Throws<PermanentException>(() => ComRetry.Run<int>(
            () =>
            {
                attempt++;
                throw new PermanentException();
            },
            () => reconnectCount++,
            ex => ex is TransientException));

        Assert.Equal(1, attempt);
        Assert.Equal(0, reconnectCount);
    }
}
