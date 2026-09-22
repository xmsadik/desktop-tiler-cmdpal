using System;
using System.Threading.Tasks;
using DesktopTiler.Core.VirtualDesktops;
using Xunit;

namespace DesktopTiler.Tests;

public class TaskTimeoutExtensionsTests
{
    [Fact]
    public void WaitOrTimeout_CompletesInTime_ReturnsResult()
    {
        var task = Task.FromResult(42);

        var result = task.WaitOrTimeout(TimeSpan.FromSeconds(1));

        Assert.Equal(42, result);
    }

    [Fact]
    public void WaitOrTimeout_NonGeneric_CompletesInTime_ReturnsNormally()
    {
        var task = Task.CompletedTask;

        task.WaitOrTimeout(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void WaitOrTimeout_DoesNotComplete_ThrowsExplorerNotResponding()
    {
        var tcs = new TaskCompletionSource<int>();

        Assert.Throws<ExplorerNotRespondingException>(() => tcs.Task.WaitOrTimeout(TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void WaitOrTimeout_NonGeneric_DoesNotComplete_ThrowsExplorerNotResponding()
    {
        var tcs = new TaskCompletionSource();

        Assert.Throws<ExplorerNotRespondingException>(() => tcs.Task.WaitOrTimeout(TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void WaitOrTimeout_FaultsWithinTimeout_RethrowsOriginalExceptionUnwrapped()
    {
        var tcs = new TaskCompletionSource<int>();
        tcs.SetException(new InvalidOperationException("boom"));

        var ex = Assert.Throws<InvalidOperationException>(() => tcs.Task.WaitOrTimeout(TimeSpan.FromSeconds(1)));
        Assert.Equal("boom", ex.Message);
    }
}
