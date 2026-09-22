using DesktopTiler.Core.Layouts;
using Xunit;

namespace DesktopTiler.Tests;

public class LayoutEngineTests
{
    [Fact]
    public void Compute_ZeroWindows_ReturnsEmptyForEveryKind()
    {
        foreach (var kind in Enum.GetValues<LayoutKind>())
        {
            Assert.Empty(LayoutEngine.Compute(kind, new Rect(0, 0, 1920, 1040), 0, 8, 0.55));
        }
    }

    [Theory]
    [InlineData(LayoutKind.MasterStack)]
    [InlineData(LayoutKind.Columns)]
    [InlineData(LayoutKind.Grid)]
    [InlineData(LayoutKind.Monocle)]
    [InlineData(LayoutKind.CenterMaster)]
    public void Compute_DispatchesToTheMatchingLayout(LayoutKind kind)
    {
        var workArea = new Rect(0, 0, 1923, 1041);
        var expected = kind switch
        {
            LayoutKind.MasterStack => MasterStackLayout.Compute(workArea, 4, 8, 0.55),
            LayoutKind.Columns => ColumnsLayout.Compute(workArea, 4, 8),
            LayoutKind.Grid => GridLayout.Compute(workArea, 4, 8),
            LayoutKind.Monocle => MonocleLayout.Compute(workArea, 4),
            LayoutKind.CenterMaster => CenterMasterLayout.Compute(workArea, 4, 8, 0.55),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        Assert.Equal(expected, LayoutEngine.Compute(kind, workArea, 4, 8, 0.55));
    }

    [Fact]
    public void Compute_ClampsMasterRatioBeforeDispatching()
    {
        var workArea = new Rect(0, 0, 1923, 1041);

        var viaEngine = LayoutEngine.Compute(LayoutKind.MasterStack, workArea, 4, 8, 50.0);
        var viaDirectClampedCall = MasterStackLayout.Compute(workArea, 4, 8, 0.9);

        Assert.Equal(viaDirectClampedCall, viaEngine);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 0, 3, 3)]
    public void Compute_DegenerateOrTinyWorkArea_NeverThrowsForAnyLayout(int left, int top, int right, int bottom)
    {
        // MonitorWorkArea.Get can return Rect(0,0,0,0) if GetMonitorInfo fails, and a real
        // monitor can still be pathologically tiny - neither should ever throw (e.g. from
        // Math.Clamp on an inverted [min,max] range once usable space collapses to <= 0).
        var workArea = new Rect(left, top, right, bottom);

        foreach (var kind in Enum.GetValues<LayoutKind>())
        {
            var result = LayoutEngine.Compute(kind, workArea, 5, 8, 0.55);
            Assert.Equal(5, result.Length);
        }
    }
}
