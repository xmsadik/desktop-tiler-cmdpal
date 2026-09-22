using System;
using System.Collections.Generic;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>
/// Pure parsing of the <c>VirtualDesktopIDs</c> REG_BINARY value: a flat byte array that is a
/// concatenation of 16-byte GUIDs, in display order. No registry access here - this is the
/// unit-testable half of <see cref="RegistryDesktopReader"/>.
/// </summary>
public static class VirtualDesktopIdParser
{
    public static IReadOnlyList<Guid> Parse(byte[]? data)
    {
        if (data is null || data.Length == 0)
        {
            return [];
        }

        var count = data.Length / 16;
        var result = new Guid[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = new Guid(data.AsSpan(i * 16, 16));
        }

        return result;
    }
}
