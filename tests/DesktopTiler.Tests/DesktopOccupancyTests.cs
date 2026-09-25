using System;
using DesktopTiler.Core.Windows;
using Xunit;

namespace DesktopTiler.Tests;

public class DesktopOccupancyTests
{
    private const int OwnPid = 1000;
    private const string CmdPalImage = WindowEnumerator.CmdPalImageName;

    private static WindowDescriptor App(nint handle = 1, int pid = 5000, string image = "notepad.exe", string className = "Notepad") =>
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
            IsOnCurrentVirtualDesktop: false,
            MonitorHandle: 1);

    private static bool IsApp(WindowDescriptor w) => DesktopOccupancy.IsAppWindow(w, OwnPid, CmdPalImage);

    [Fact]
    public void IsAppWindow_PlainAppWindow_ReturnsTrue() => Assert.True(IsApp(App()));

    [Fact]
    public void IsAppWindow_Minimized_ReturnsTrue() => Assert.True(IsApp(App() with { IsMinimized = true }));

    [Fact]
    public void IsAppWindow_CloakedOnOtherDesktop_ReturnsTrue() => Assert.True(IsApp(App() with { IsCloaked = true }));

    [Fact]
    public void IsAppWindow_Hung_ReturnsTrue() => Assert.True(IsApp(App() with { IsHung = true }));

    [Fact]
    public void IsAppWindow_NoThickFrame_ReturnsTrue() => Assert.True(IsApp(App() with { HasThickFrame = false }));

    [Fact]
    public void IsAppWindow_NotVisible_ReturnsFalse() => Assert.False(IsApp(App() with { IsVisible = false }));

    [Fact]
    public void IsAppWindow_ToolWindow_ReturnsFalse() => Assert.False(IsApp(App() with { IsToolWindow = true }));

    [Fact]
    public void IsAppWindow_Owned_ReturnsFalse() => Assert.False(IsApp(App() with { HasOwner = true }));

    [Fact]
    public void IsAppWindow_Untitled_ReturnsFalse() => Assert.False(IsApp(App() with { HasTitle = false }));

    [Fact]
    public void IsAppWindow_OwnProcess_ReturnsFalse() => Assert.False(IsApp(App(pid: OwnPid)));

    [Fact]
    public void IsAppWindow_CmdPal_ReturnsFalse() => Assert.False(IsApp(App(image: CmdPalImage)));

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    public void IsAppWindow_ShellWindow_ReturnsFalse(string className) =>
        Assert.False(IsApp(App(className: className)));

    [Fact]
    public void OccupiedDesktops_DistinctIdsCollected_EmptyIgnored()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var occupied = DesktopOccupancy.OccupiedDesktops([a, Guid.Empty, a, b]);

        Assert.Equal(2, occupied.Count);
        Assert.Contains(a, occupied);
        Assert.Contains(b, occupied);
        Assert.DoesNotContain(Guid.Empty, occupied);
    }

    [Fact]
    public void OccupiedDesktops_NoWindows_ReturnsEmptySet() =>
        Assert.Empty(DesktopOccupancy.OccupiedDesktops([]));
}
