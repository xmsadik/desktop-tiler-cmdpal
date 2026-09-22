using System.Collections.Generic;
using DesktopTiler.Core.Windows;
using Xunit;

namespace DesktopTiler.Tests;

public class TargetWindowSelectorTests
{
    private const int OwnPid = 1000;
    private const string CmdPalImage = WindowEnumerator.CmdPalImageName;

    private static WindowDescriptor Tileable(nint handle, int pid = 5000, string image = "notepad.exe", string className = "Notepad", nint monitor = 1) =>
        new(
            Handle: handle,
            ProcessId: pid,
            ImageName: image,
            ClassName: className,
            IsVisible: true,
            IsToolWindow: false,
            HasOwner: false,
            IsMinimized: false,
            IsCloaked: false,
            IsHung: false,
            HasThickFrame: true,
            HasTitle: true,
            IsOnCurrentVirtualDesktop: true,
            MonitorHandle: monitor);

    [Fact]
    public void IsTileable_AllConditionsMet_ReturnsTrue()
    {
        Assert.True(TargetWindowSelector.IsTileable(Tileable(1)));
    }

    [Fact]
    public void IsTileable_NotVisible_ReturnsFalse() =>
        Assert.False(TargetWindowSelector.IsTileable(Tileable(1) with { IsVisible = false }));

    [Fact]
    public void IsTileable_ToolWindow_ReturnsFalse() =>
        Assert.False(TargetWindowSelector.IsTileable(Tileable(1) with { IsToolWindow = true }));

    [Fact]
    public void IsTileable_HasOwner_ReturnsFalse() =>
        Assert.False(TargetWindowSelector.IsTileable(Tileable(1) with { HasOwner = true }));

    [Fact]
    public void IsTileable_Minimized_ReturnsFalse() =>
        Assert.False(TargetWindowSelector.IsTileable(Tileable(1) with { IsMinimized = true }));

    [Fact]
    public void IsTileable_Cloaked_ReturnsFalse() =>
        Assert.False(TargetWindowSelector.IsTileable(Tileable(1) with { IsCloaked = true }));

    [Fact]
    public void IsTileable_Hung_ReturnsFalse() =>
        Assert.False(TargetWindowSelector.IsTileable(Tileable(1) with { IsHung = true }));

    [Fact]
    public void IsTileable_NoThickFrame_ReturnsFalse() =>
        Assert.False(TargetWindowSelector.IsTileable(Tileable(1) with { HasThickFrame = false }));

    [Fact]
    public void IsTileable_NoTitle_ReturnsFalse() =>
        Assert.False(TargetWindowSelector.IsTileable(Tileable(1) with { HasTitle = false }));

    [Fact]
    public void IsTileable_NotOnCurrentVirtualDesktop_ReturnsFalse() =>
        Assert.False(TargetWindowSelector.IsTileable(Tileable(1) with { IsOnCurrentVirtualDesktop = false }));

    [Fact]
    public void SelectTarget_ForegroundIsTileableAndNotExcluded_ReturnsForeground()
    {
        var foreground = Tileable(1);
        var other = Tileable(2);

        var result = TargetWindowSelector.SelectTarget(foreground, [foreground, other], OwnPid, CmdPalImage);

        Assert.Equal(foreground, result);
    }

    [Fact]
    public void SelectTarget_ForegroundIsCmdPal_FallsBackToTopmostTileable()
    {
        var foreground = Tileable(1, pid: 42, image: CmdPalImage);
        var topmostTileable = Tileable(2);
        var another = Tileable(3);

        var result = TargetWindowSelector.SelectTarget(foreground, [foreground, topmostTileable, another], OwnPid, CmdPalImage);

        Assert.Equal(topmostTileable, result);
    }

    [Fact]
    public void SelectTarget_ForegroundIsOwnProcess_FallsBackToTopmostTileable()
    {
        var foreground = Tileable(1, pid: OwnPid);
        var topmostTileable = Tileable(2);

        var result = TargetWindowSelector.SelectTarget(foreground, [foreground, topmostTileable], OwnPid, CmdPalImage);

        Assert.Equal(topmostTileable, result);
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    public void SelectTarget_ForegroundIsShellWindow_FallsBackToTopmostTileable(string shellClass)
    {
        var foreground = Tileable(1, className: shellClass);
        var topmostTileable = Tileable(2);

        var result = TargetWindowSelector.SelectTarget(foreground, [foreground, topmostTileable], OwnPid, CmdPalImage);

        Assert.Equal(topmostTileable, result);
    }

    [Fact]
    public void SelectTarget_ForegroundNotTileable_FallsBackToTopmostTileable()
    {
        var foreground = Tileable(1) with { IsMinimized = true };
        var topmostTileable = Tileable(2);

        var result = TargetWindowSelector.SelectTarget(foreground, [foreground, topmostTileable], OwnPid, CmdPalImage);

        Assert.Equal(topmostTileable, result);
    }

    [Fact]
    public void SelectTarget_NoForegroundGiven_UsesTopmostTileableFromZOrder()
    {
        var shell = Tileable(1, className: "Shell_TrayWnd");
        var topmostTileable = Tileable(2);

        var result = TargetWindowSelector.SelectTarget(null, [shell, topmostTileable], OwnPid, CmdPalImage);

        Assert.Equal(topmostTileable, result);
    }

    [Fact]
    public void SelectTarget_NoTileableWindowsAtAll_ReturnsNull()
    {
        var shell = Tileable(1, className: "Progman");
        var minimized = Tileable(2) with { IsMinimized = true };

        var result = TargetWindowSelector.SelectTarget(null, [shell, minimized], OwnPid, CmdPalImage);

        Assert.Null(result);
    }

    [Fact]
    public void SelectCandidates_TargetFirst_ThenSameMonitorTileableWindowsInZOrder()
    {
        var target = Tileable(1, monitor: 100);
        var sameMonitor = Tileable(2, monitor: 100);
        var otherMonitor = Tileable(3, monitor: 200);
        var notTileable = Tileable(4, monitor: 100) with { IsMinimized = true };

        var candidates = TargetWindowSelector.SelectCandidates([target, sameMonitor, otherMonitor, notTileable], target);

        Assert.Equal([target, sameMonitor], candidates);
    }

    [Fact]
    public void SelectCandidates_TargetNotDuplicatedIfPresentInZOrder()
    {
        var target = Tileable(1, monitor: 100);

        var candidates = TargetWindowSelector.SelectCandidates([target], target);

        var single = Assert.Single(candidates);
        Assert.Equal(target, single);
    }
}
