using DesktopTiler.Core.Layouts;
using Xunit;

namespace DesktopTiler.Tests;

public class MonocleLayoutTests
{
    [Fact]
    public void Compute_ZeroWindows_ReturnsEmpty()
    {
        Assert.Empty(MonocleLayout.Compute(new Rect(0, 0, 1920, 1040), 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Compute_EveryWindow_GetsTheFullWorkAreaWithNoGaps(int n)
    {
        var workArea = new Rect(0, 0, 1920, 1040);
        var result = MonocleLayout.Compute(workArea, n);

        Assert.Equal(n, result.Length);
        Assert.All(result, cell => Assert.Equal(workArea, cell));
    }

    [Fact]
    public void Compute_NegativeOriginWorkArea_EveryWindowGetsTheFullArea()
    {
        var workArea = new Rect(-1920, -200, 0, 880);
        var result = MonocleLayout.Compute(workArea, 3);

        Assert.Equal(3, result.Length);
        Assert.All(result, cell => Assert.Equal(workArea, cell));
    }
}
