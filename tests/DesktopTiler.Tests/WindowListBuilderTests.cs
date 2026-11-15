using System;
using System.Linq;
using DesktopTiler.Core.VirtualDesktops;
using DesktopTiler.Core.Windows;
using Xunit;

namespace DesktopTiler.Tests;

public class WindowListBuilderTests
{
    private static readonly Guid D1 = Guid.NewGuid();
    private static readonly Guid D2 = Guid.NewGuid();
    private static readonly Guid D3 = Guid.NewGuid();

    private static readonly DesktopInfo[] Desktops =
    [
        new(D1, 0, "Office"),
        new(D2, 1, null),
        new(D3, 2, "Coding"),
    ];

    private static WindowListInput W(nint handle, Guid desktop) => new(handle, $"Window {handle}", $@"C:\apps\app{handle}.exe", desktop);

    [Fact]
    public void Build_GroupsByDesktopDisplayOrder_KeepingZOrderWithinEachDesktop()
    {
        var input = new[] { W(1, D3), W(2, D1), W(3, D3), W(4, D2), W(5, D1) };

        var result = WindowListBuilder.Build(input, Desktops);

        Assert.Equal(new nint[] { 2, 5, 4, 1, 3 }, result.Select(e => e.Handle));
        Assert.Equal(new[] { D1, D1, D2, D3, D3 }, result.Select(e => e.Desktop!.Value.Id));
    }

    [Fact]
    public void Build_UnknownDesktopId_GoesLastWithNoDesktop()
    {
        var pinned = Guid.NewGuid();
        var input = new[] { W(1, pinned), W(2, D2) };

        var result = WindowListBuilder.Build(input, Desktops);

        Assert.Equal(new nint[] { 2, 1 }, result.Select(e => e.Handle));
        Assert.Null(result[1].Desktop);
    }

    [Fact]
    public void Build_UnreadableDesktopId_IsDropped()
    {
        var input = new[] { W(1, Guid.Empty), W(2, D1) };

        var result = WindowListBuilder.Build(input, Desktops);

        Assert.Single(result);
        Assert.Equal(2, result[0].Handle);
    }

    [Fact]
    public void Build_CarriesTitlePathAndDesktopInfo()
    {
        var result = WindowListBuilder.Build([W(7, D3)], Desktops);

        var entry = Assert.Single(result);
        Assert.Equal("Window 7", entry.Title);
        Assert.Equal(@"C:\apps\app7.exe", entry.ImagePath);
        Assert.Equal(new DesktopInfo(D3, 2, "Coding"), entry.Desktop);
    }

    [Fact]
    public void Build_NoWindows_ReturnsEmpty() => Assert.Empty(WindowListBuilder.Build([], Desktops));
}
