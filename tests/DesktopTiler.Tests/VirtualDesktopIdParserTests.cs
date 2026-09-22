using System;
using DesktopTiler.Core.VirtualDesktops;
using Xunit;

namespace DesktopTiler.Tests;

public class VirtualDesktopIdParserTests
{
    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        Assert.Empty(VirtualDesktopIdParser.Parse(null));
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsEmpty()
    {
        Assert.Empty(VirtualDesktopIdParser.Parse([]));
    }

    [Fact]
    public void Parse_SingleGuid_RoundTrips()
    {
        var id = Guid.NewGuid();
        var bytes = id.ToByteArray();

        var result = VirtualDesktopIdParser.Parse(bytes);

        var single = Assert.Single(result);
        Assert.Equal(id, single);
    }

    [Fact]
    public void Parse_MultipleGuids_PreservesOrder()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var bytes = new byte[16 * ids.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i].ToByteArray().CopyTo(bytes, i * 16);
        }

        var result = VirtualDesktopIdParser.Parse(bytes);

        Assert.Equal(ids, result);
    }

    [Fact]
    public void Parse_TrailingPartialGuid_IsIgnored()
    {
        var id = Guid.NewGuid();
        var bytes = new byte[16 + 5];
        id.ToByteArray().CopyTo(bytes, 0);

        var result = VirtualDesktopIdParser.Parse(bytes);

        var single = Assert.Single(result);
        Assert.Equal(id, single);
    }
}
