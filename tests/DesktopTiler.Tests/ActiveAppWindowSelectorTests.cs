using DesktopTiler.Core.Windows;
using Xunit;

namespace DesktopTiler.Tests;

public class ActiveAppWindowSelectorTests
{
    private const int OwnPid = 1000;
    private const string CmdPalImage = WindowEnumerator.CmdPalImageName;

    private static WindowDescriptor App(nint handle, int pid = 5000, string image = "notepad.exe") =>
        new(
            Handle: handle,
            ProcessId: pid,
            ImageName: image,
            ClassName: "Notepad",
            IsVisible: true,
            IsToolWindow: false,
            HasOwner: false,
            IsMinimized: false,
            IsCloaked: false,
            IsHung: false,
            HasThickFrame: false, // fixed-size: not tileable, but still movable
            HasTitle: true,
            IsOnCurrentVirtualDesktop: true,
            MonitorHandle: 1);

    private static WindowDescriptor? Select(WindowDescriptor? foreground, params WindowDescriptor[] zOrder) =>
        TargetWindowSelector.SelectActiveAppWindow(foreground, zOrder, OwnPid, CmdPalImage);

    [Fact]
    public void ForegroundAppWindow_IsChosen() => Assert.Equal(7, Select(App(7), App(1), App(2))?.Handle);

    [Fact]
    public void ForegroundIsCommandPalette_FallsBackToTopZOrderWindow() =>
        Assert.Equal(1, Select(App(9, pid: 42, image: CmdPalImage), App(9, pid: 42, image: CmdPalImage), App(1), App(2))?.Handle);

    [Fact]
    public void FallbackSkipsMinimizedAndOtherDesktopWindows() =>
        Assert.Equal(3, Select(null, App(1) with { IsMinimized = true }, App(2) with { IsOnCurrentVirtualDesktop = false }, App(3))?.Handle);

    [Fact]
    public void CloakedForeground_IsNotChosen() => Assert.Equal(2, Select(App(1) with { IsCloaked = true }, App(2))?.Handle);

    [Fact]
    public void MaximizedOrFixedSizeWindow_IsMovable() => Assert.True(TargetWindowSelector.IsMovable(App(1), OwnPid, CmdPalImage));

    [Fact]
    public void OwnProcessWindow_IsNotMovable() => Assert.False(TargetWindowSelector.IsMovable(App(1, pid: OwnPid), OwnPid, CmdPalImage));

    [Fact]
    public void NoCandidate_ReturnsNull() => Assert.Null(Select(null, App(1) with { HasOwner = true }));
}
