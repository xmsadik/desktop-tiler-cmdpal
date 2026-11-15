using DesktopTiler.Core.VirtualDesktops;
using Xunit;

namespace DesktopTiler.Tests;

public class DesktopManagementRulesTests
{
    [Theory]
    [InlineData(3, 1, 0)] // middle -> left neighbour
    [InlineData(3, 2, 1)] // last -> left neighbour
    [InlineData(3, 0, 1)] // first -> the next one
    [InlineData(2, 0, 1)]
    public void FallbackIndex_PicksLeftNeighbourOrSecond(int count, int index, int expected)
    {
        Assert.Equal(expected, DesktopManagementRules.FallbackIndex(count, index));
    }

    [Theory]
    [InlineData(1, 0)] // the only desktop can't be removed
    [InlineData(0, 0)]
    [InlineData(3, 3)] // out of range
    [InlineData(3, -1)]
    public void FallbackIndex_NullWhenNotRemovable(int count, int index)
    {
        Assert.Null(DesktopManagementRules.FallbackIndex(count, index));
    }

    [Theory]
    [InlineData(3, 1, -1, 0)]
    [InlineData(3, 1, +1, 2)]
    [InlineData(3, 0, +1, 1)]
    public void MoveTargetIndex_MovesOnePlace(int count, int index, int step, int expected)
    {
        Assert.Equal(expected, DesktopManagementRules.MoveTargetIndex(count, index, step));
    }

    [Theory]
    [InlineData(3, 0, -1)] // already first
    [InlineData(3, 2, +1)] // already last
    [InlineData(1, 0, +1)]
    [InlineData(3, 5, -1)] // unknown position
    public void MoveTargetIndex_NullPastEitherEnd(int count, int index, int step)
    {
        Assert.Null(DesktopManagementRules.MoveTargetIndex(count, index, step));
    }

    [Theory]
    [InlineData("  Mail  ", "Mail")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    [InlineData("İş · Çağrı", "İş · Çağrı")]
    public void NormalizeName_TrimsAndBlankMeansNoName(string? input, string expected)
    {
        Assert.Equal(expected, DesktopManagementRules.NormalizeName(input));
    }

    [Fact]
    public void NormalizeName_CapsLength()
    {
        var name = DesktopManagementRules.NormalizeName(new string('a', 300));
        Assert.Equal(DesktopManagementRules.MaxNameLength, name.Length);
    }
}
