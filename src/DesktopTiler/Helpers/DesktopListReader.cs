using System;
using System.Collections.Generic;
using DesktopTiler.Core.VirtualDesktops;

namespace DesktopTiler;

/// <summary>
/// Shared "the registry has no desktops - fall back to the COM enumeration" logic for
/// <see cref="DesktopsPage"/> and the desktop commands (<see cref="DesktopNumberCommand"/>,
/// <see cref="StepDesktopCommand"/>). A fresh Windows profile that has never opened Task View has
/// no <c>HKCU\...\Explorer\VirtualDesktops</c> key at all, so
/// <see cref="RegistryDesktopReader.ReadDesktops"/> returns an empty list even though Windows
/// always has at least one (unnamed, unindexed-by-the-registry) virtual desktop. Falling back to
/// <see cref="VdComClient.GetDesktopIdsAsync"/> (the COM enumeration - otherwise unused until now)
/// recovers that single desktop, with no name (the registry is the only source of desktop names).
/// </summary>
internal static class DesktopListReader
{
    /// <summary>Never throws: any failure (COM error, <see cref="UnsupportedBuildException"/>,
    /// <see cref="ExplorerNotRespondingException"/>) is swallowed and an empty list returned, so
    /// callers keep showing whatever message they already show for "no desktops found".</summary>
    public static IReadOnlyList<DesktopInfo> FallbackToVdComClient(VdComClient vdClient)
    {
        try
        {
            var ids = vdClient.GetDesktopIdsAsync().WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
            if (ids.Count == 0)
            {
                return [];
            }

            var result = new DesktopInfo[ids.Count];
            for (var i = 0; i < ids.Count; i++)
            {
                result[i] = new DesktopInfo(ids[i], i, Name: null);
            }

            return result;
        }
        catch (Exception)
        {
            return [];
        }
    }
}
