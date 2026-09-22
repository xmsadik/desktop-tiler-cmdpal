using System;
using System.Runtime.InteropServices;
using DesktopTiler.Core.VirtualDesktops;
using Xunit;

namespace DesktopTiler.Tests;

public class VdConnectionStateTests
{
    private const int RegDbEClassNotReg = unchecked((int)0x80040154);

    [Fact]
    public void Initially_NotUnsupported()
    {
        var state = new VdConnectionState();

        Assert.False(state.IsUnsupported);
    }

    [Fact]
    public void RecordFailure_TransientException_StaysNotUnsupported()
    {
        var state = new VdConnectionState();

        state.RecordFailure(new InvalidOperationException("RPC_S_SERVER_UNAVAILABLE"));

        Assert.False(state.IsUnsupported);
    }

    [Fact]
    public void RecordFailure_ComExceptionWithClassNotRegHResult_StaysNotUnsupported()
    {
        // The policy is exception-TYPE based, not HRESULT based: a COMException carrying
        // REGDB_E_CLASSNOTREG (e.g. from CoCreateInstance(ImmersiveShell) while Explorer is still
        // starting) must never latch, even though that HRESULT numerically equals one of the two
        // that used to auto-convert to UnsupportedBuildException. Only an actual
        // UnsupportedBuildException instance latches - see VdComClient.EnsureInternalConnected,
        // which only constructs one after CoCreateInstance already succeeded.
        var state = new VdConnectionState();

#pragma warning disable CA2201 // COMException is exactly the right type to simulate here; production code throws it the same way.
        state.RecordFailure(new COMException("classnotreg", RegDbEClassNotReg));
#pragma warning restore CA2201

        Assert.False(state.IsUnsupported);
    }

    [Fact]
    public void RecordFailure_UnsupportedBuildException_LatchesSticky()
    {
        var state = new VdConnectionState();
        var ex = new UnsupportedBuildException("E_NOINTERFACE");

        state.RecordFailure(ex);

        Assert.True(state.IsUnsupported);
        Assert.Same(ex, state.Sticky);
    }

    [Fact]
    public void RecordFailure_UnsupportedThenTransient_StaysSticky()
    {
        var state = new VdConnectionState();
        var ex = new UnsupportedBuildException("E_NOINTERFACE");

        state.RecordFailure(ex);
        state.RecordFailure(new InvalidOperationException("some other failure"));

        Assert.True(state.IsUnsupported);
        Assert.Same(ex, state.Sticky);
    }

    [Fact]
    public void Sticky_WhenNotUnsupported_Throws()
    {
        var state = new VdConnectionState();

        Assert.Throws<InvalidOperationException>(() => state.Sticky);
    }
}
