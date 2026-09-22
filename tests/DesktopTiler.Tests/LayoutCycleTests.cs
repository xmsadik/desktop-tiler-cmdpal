using DesktopTiler.Core.Layouts;
using Xunit;

namespace DesktopTiler.Tests;

public class LayoutCycleTests
{
    [Fact]
    public void Last_BeforeAnyRecord_IsNull()
    {
        Assert.Null(new LayoutCycle().Last);
    }

    [Fact]
    public void Retile_BeforeAnyRecord_ReturnsDefault()
    {
        Assert.Equal(LayoutKind.MasterStack, new LayoutCycle().Retile());
    }

    [Fact]
    public void Next_BeforeAnyRecord_ReturnsDefault()
    {
        Assert.Equal(LayoutKind.MasterStack, new LayoutCycle().Next());
    }

    [Fact]
    public void Record_UpdatesLastAndRetile()
    {
        var cycle = new LayoutCycle();
        cycle.Record(LayoutKind.Grid);

        Assert.Equal(LayoutKind.Grid, cycle.Last);
        Assert.Equal(LayoutKind.Grid, cycle.Retile());
    }

    [Theory]
    [InlineData(LayoutKind.MasterStack, LayoutKind.Columns)]
    [InlineData(LayoutKind.Columns, LayoutKind.Grid)]
    [InlineData(LayoutKind.Grid, LayoutKind.Monocle)]
    [InlineData(LayoutKind.Monocle, LayoutKind.CenterMaster)]
    [InlineData(LayoutKind.CenterMaster, LayoutKind.MasterStack)]
    public void Next_AdvancesInDeclarationOrder_WrappingFromCenterMasterToMasterStack(LayoutKind last, LayoutKind expectedNext)
    {
        var cycle = new LayoutCycle();
        cycle.Record(last);

        Assert.Equal(expectedNext, cycle.Next());
    }

    [Fact]
    public void Next_DoesNotRecord_CallingItTwiceReturnsTheSameValue()
    {
        var cycle = new LayoutCycle();
        cycle.Record(LayoutKind.Columns);

        Assert.Equal(cycle.Next(), cycle.Next());
    }

    [Fact]
    public void Advance_FromNull_ReturnsDefaultThenColumns()
    {
        var cycle = new LayoutCycle();

        var first = cycle.Advance();
        Assert.Equal(LayoutKind.MasterStack, first);
        Assert.Equal(LayoutKind.MasterStack, cycle.Last);

        var second = cycle.Advance();
        Assert.Equal(LayoutKind.Columns, second);
        Assert.Equal(LayoutKind.Columns, cycle.Last);
    }

    [Fact]
    public void Advance_RecordsSoRetileAndLastReflectTheAdvancedValue()
    {
        var cycle = new LayoutCycle();
        cycle.Record(LayoutKind.Grid);

        var advanced = cycle.Advance();

        Assert.Equal(LayoutKind.Monocle, advanced);
        Assert.Equal(LayoutKind.Monocle, cycle.Last);
        Assert.Equal(LayoutKind.Monocle, cycle.Retile());
    }

    [Fact]
    public void ConstructorDefaultKind_NullDelegate_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LayoutCycle(null!));
    }

    [Fact]
    public void ChangedDefault_IsHonouredBeforeFirstTile_AndIgnoredAfterOne()
    {
        var configuredDefault = LayoutKind.MasterStack;
        var cycle = new LayoutCycle(() => configuredDefault);

        // Before anything has been tiled, Retile/Next track the (possibly-changing) default live.
        Assert.Equal(LayoutKind.MasterStack, cycle.Retile());
        Assert.Equal(LayoutKind.MasterStack, cycle.Next());

        configuredDefault = LayoutKind.Grid;
        Assert.Equal(LayoutKind.Grid, cycle.Retile());
        Assert.Equal(LayoutKind.Grid, cycle.Next());

        // Once something has been tiled, the recorded value wins - the default delegate is no
        // longer consulted even if it changes again afterward.
        cycle.Record(LayoutKind.Columns);
        configuredDefault = LayoutKind.Monocle;

        Assert.Equal(LayoutKind.Columns, cycle.Last);
        Assert.Equal(LayoutKind.Columns, cycle.Retile());
        Assert.Equal(LayoutKind.Grid, cycle.Next());
    }
}
