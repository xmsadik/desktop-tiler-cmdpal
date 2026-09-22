using System;
using DesktopTiler.Core.Layouts;
using Xunit;

namespace DesktopTiler.Tests;

public class TileMemoryStoreTests
{
    private static readonly Guid DesktopA = Guid.NewGuid();
    private static readonly Guid DesktopB = Guid.NewGuid();
    private const nint Monitor1 = 1;
    private const nint Monitor2 = 2;

    private static TileMemory Memory(nint monitor, int seed) =>
        new(monitor, Order: [new WindowId(seed, ProcessId: 1)], TargetAtRecord: new WindowId(seed, ProcessId: 1));

    [Fact]
    public void Get_BeforeAnyRecord_ReturnsNull()
    {
        Assert.Null(new TileMemoryStore().Get(DesktopA, Monitor1));
    }

    [Fact]
    public void Record_ThenGet_SameDesktopAndMonitor_ReturnsIt()
    {
        var store = new TileMemoryStore();
        var memory = Memory(Monitor1, 1);

        store.Record(DesktopA, Monitor1, memory);

        Assert.Equal(memory, store.Get(DesktopA, Monitor1));
    }

    [Fact]
    public void Get_DifferentDesktop_SameMonitor_DoesNotSeeOtherDesktopsMemory()
    {
        var store = new TileMemoryStore();
        store.Record(DesktopA, Monitor1, Memory(Monitor1, 1));

        Assert.Null(store.Get(DesktopB, Monitor1));
    }

    [Fact]
    public void Get_SameDesktop_DifferentMonitor_DoesNotSeeOtherMonitorsMemory()
    {
        var store = new TileMemoryStore();
        store.Record(DesktopA, Monitor1, Memory(Monitor1, 1));

        Assert.Null(store.Get(DesktopA, Monitor2));
    }

    [Fact]
    public void Record_TwiceForSameKey_OverwritesRatherThanDuplicating()
    {
        var store = new TileMemoryStore();
        store.Record(DesktopA, Monitor1, Memory(Monitor1, 1));
        var second = Memory(Monitor1, 2);
        store.Record(DesktopA, Monitor1, second);

        Assert.Equal(second, store.Get(DesktopA, Monitor1));
    }

    [Fact]
    public void Record_SwitchingBetweenTwoDesktops_EachKeepsItsOwnOrder()
    {
        var store = new TileMemoryStore();
        var forA = Memory(Monitor1, 1);
        var forB = Memory(Monitor1, 2);

        store.Record(DesktopA, Monitor1, forA);
        store.Record(DesktopB, Monitor1, forB);

        // Switching back to desktop A must still see A's memory, not B's.
        Assert.Equal(forA, store.Get(DesktopA, Monitor1));
        Assert.Equal(forB, store.Get(DesktopB, Monitor1));
    }

    [Fact]
    public void Record_BeyondCap_DropsOldestKey()
    {
        var store = new TileMemoryStore();

        // One more than the 32-entry cap - all distinct (desktop, monitor) keys.
        var keys = new Guid[33];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = Guid.NewGuid();
            store.Record(keys[i], Monitor1, Memory(Monitor1, i));
        }

        Assert.Null(store.Get(keys[0], Monitor1)); // oldest - evicted
        Assert.NotNull(store.Get(keys[1], Monitor1)); // second-oldest - still present
        Assert.NotNull(store.Get(keys[32], Monitor1)); // newest - present
    }
}
