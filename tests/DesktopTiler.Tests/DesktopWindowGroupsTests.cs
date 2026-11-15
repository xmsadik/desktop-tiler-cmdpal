using System;
using System.Collections.Generic;
using System.Linq;
using DesktopTiler.Core.Windows;
using Xunit;

namespace DesktopTiler.Tests;

public class DesktopWindowGroupsTests
{
    private static readonly Guid D1 = Guid.NewGuid();
    private static readonly Guid D2 = Guid.NewGuid();

    [Fact]
    public void Group_KeepsOrderPerDesktop_AndSkipsUnreadableIds()
    {
        var groups = DesktopWindowGroups.Group([1, 2, 3, 4], [D2, D1, Guid.Empty, D2]);

        Assert.Equal(2, groups.Count);
        Assert.Equal(new nint[] { 1, 4 }, groups[D2]);
        Assert.Equal(new nint[] { 2 }, groups[D1]);
    }

    [Fact]
    public void SameMembership_IgnoresOrder()
    {
        var a = new Dictionary<Guid, List<nint>> { [D1] = [1, 2] };
        var b = new Dictionary<Guid, List<nint>> { [D1] = [2, 1] };

        Assert.True(DesktopWindowGroups.SameMembership(a, b));
    }

    [Fact]
    public void SameMembership_WindowMovedBetweenDesktops_IsDifferent()
    {
        var a = new Dictionary<Guid, List<nint>> { [D1] = [1, 2] };
        var b = new Dictionary<Guid, List<nint>> { [D1] = [1], [D2] = [2] };

        Assert.False(DesktopWindowGroups.SameMembership(a, b));
    }

    [Fact]
    public void TooltipSubtitle_NameThenBulletedTitles() =>
        Assert.Equal("Office\n• Outlook\n• Teams", DesktopWindowGroups.TooltipSubtitle("Office", ["Outlook", " ", "Teams "]));

    [Fact]
    public void TooltipSubtitle_LongTitle_IsCutWithEllipsis()
    {
        var line = DesktopWindowGroups.TooltipSubtitle(null, [new string('x', 100)]).Split('\n')[1];

        Assert.Equal("• " + new string('x', DesktopWindowGroups.MaxTooltipTitleLength) + "…", line);
    }

    [Fact]
    public void TooltipSubtitle_NoNameNoWindows_IsEmpty() =>
        Assert.Equal(string.Empty, DesktopWindowGroups.TooltipSubtitle(null, []));

    [Fact]
    public void TooltipSubtitle_TooManyTitles_SummarisesTheRest()
    {
        var titles = Enumerable.Range(1, DesktopWindowGroups.MaxTooltipTitles + 3).Select(i => $"W{i}").ToArray();

        var lines = DesktopWindowGroups.TooltipSubtitle("D", titles).Split('\n');

        Assert.Equal(1 + DesktopWindowGroups.MaxTooltipTitles + 1, lines.Length);
        Assert.Equal("+3 more", lines[^1]);
    }
}
