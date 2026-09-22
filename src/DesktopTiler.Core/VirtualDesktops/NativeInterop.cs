using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>
/// CLSIDs for the undocumented virtual-desktop COM objects, and for the public, documented
/// IVirtualDesktopManager. Pinned to the layout observed on build 26100.9448 (24H2) during the
/// Phase 1 spike. If a future build changes these GUIDs or the vtable shape,
/// activation/QueryInterface fails with E_NOINTERFACE and <see cref="VdComClient"/> surfaces
/// <see cref="UnsupportedBuildException"/>.
/// </summary>
internal static class VdGuids
{
    public static readonly Guid ClsidImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    public static readonly Guid ClsidVirtualDesktopManagerInternal = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    public static readonly Guid ClsidVirtualDesktopManager = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");
}

/// <summary>
/// ole32.dll entry points needed to activate the virtual-desktop COM objects ourselves (rather
/// than via <c>Type.GetTypeFromCLSID</c>/<c>Activator.CreateInstance</c>, which relies on the
/// classic, non-trim-safe COM interop runtime). Trim/AOT-safe: <see cref="LibraryImportAttribute"/>
/// generates the P/Invoke marshalling at compile time.
/// </summary>
internal static partial class Ole32
{
    // CLSID_ImmersiveShell is hosted out-of-process by explorer.exe when activated from another
    // process (needs CLSCTX_LOCAL_SERVER; CLSCTX_INPROC_SERVER alone fails with
    // REGDB_E_CLASSNOTREG / 0x80040154), while the public CLSID_VirtualDesktopManager is
    // in-proc. Request both contexts for every activation so either registration works.
    internal const uint ClsctxServer = 0x1 | 0x4; // CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER
    internal const uint CoinitApartmentThreaded = 0x2;

    [LibraryImport("ole32.dll")]
    internal static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    internal static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);
}

// --- Undocumented Virtual Desktop COM interfaces --------------------------------------------
//
// Declared with [GeneratedComInterface] (source-generated COM, trim/AOT-safe) instead of the
// classic [ComImport]. [PreserveSig] on every method: we check the returned HRESULT ourselves
// rather than relying on the CLR's implicit "throw on failure" behavior, per the Phase 1 spec.
//
// Methods we don't call are still declared, in exact vtable order, as placeholders that take/
// return `nint` for any COM-interface-typed parameter - we don't know or care about their real
// managed shape, and `nint` is blittable so [GeneratedComInterface] just passes the raw pointer
// through without attempting marshalling. This keeps the vtable layout correct (every slot must
// be present, in order) without having to model interfaces (IApplicationView, IObjectArray of
// unknown-interface, etc.) that Phase 1 doesn't need.
//
// GUIDs and vtable order come from the Phase 1 spike (scratchpad/vdspike), itself based on
// Markus Scholtes' VirtualDesktop11-24H2.cs, confirmed working against build 26100.9448.

/// <summary>Standard OLE IServiceProvider (ocidl.h) - used to get IVirtualDesktopManagerInternal
/// off the immersive shell object.</summary>
[GeneratedComInterface]
[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
internal partial interface IServiceProvider10
{
    [PreserveSig]
    int QueryService(in Guid guidService, in Guid riid, out IVirtualDesktopManagerInternal? ppvObject);
}

/// <summary>Shell IObjectArray, narrowed to only ever hand back <see cref="IVirtualDesktop"/>
/// items - the only use we have for it.</summary>
[GeneratedComInterface]
[Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
internal partial interface IObjectArray
{
    [PreserveSig]
    int GetCount(out int count);

    [PreserveSig]
    int GetAt(int index, in Guid riid, out IVirtualDesktop? obj);
}

/// <summary>Undocumented per-desktop interface. Only GetId is used; the other slots are declared
/// (in order) as placeholders so the vtable layout matches the real interface.</summary>
[GeneratedComInterface]
[Guid("3F07F4BE-B107-441A-AF0F-39D82529072C")]
internal partial interface IVirtualDesktop
{
    [PreserveSig]
    int IsViewVisible(nint view, out int visible); // unused placeholder

    [PreserveSig]
    int GetId(out Guid id);

    [PreserveSig]
    int GetName(out nint hstringName); // unused placeholder (raw HSTRING, not marshaled)

    [PreserveSig]
    int GetWallpaperPath(out nint hstringPath); // unused placeholder

    [PreserveSig]
    int IsRemote(out int remote); // unused placeholder
}

/// <summary>Undocumented desktop manager. Declared in full vtable order through
/// SwitchDesktopWithAnimation/WaitForAnimationToComplete per the Phase 1 spec; only the members
/// actually needed by <see cref="VdComClient"/> are typed richly, the rest are nint placeholders.</summary>
[GeneratedComInterface]
[Guid("53F5CA0B-158F-4124-900C-057158060B27")]
internal partial interface IVirtualDesktopManagerInternal
{
    [PreserveSig]
    int GetCount(out int count); // unused (we use GetDesktops().GetCount() instead)

    [PreserveSig]
    int MoveViewToDesktop(nint view, nint desktop); // unused placeholder

    [PreserveSig]
    int CanViewMoveDesktops(nint view, out int canMove); // unused placeholder

    [PreserveSig]
    int GetCurrentDesktop(out IVirtualDesktop? desktop);

    [PreserveSig]
    int GetDesktops(out IObjectArray? desktops);

    [PreserveSig]
    int GetAdjacentDesktop(nint from, int direction, out nint desktop); // unused placeholder

    [PreserveSig]
    int SwitchDesktop(IVirtualDesktop desktop);

    [PreserveSig]
    int SwitchDesktopAndMoveForegroundView(nint desktop); // unused placeholder

    [PreserveSig]
    int CreateDesktop(out nint desktop); // unused placeholder

    [PreserveSig]
    int MoveDesktop(nint desktop, int index); // unused placeholder

    [PreserveSig]
    int RemoveDesktop(nint desktop, nint fallback); // unused placeholder

    [PreserveSig]
    int FindDesktop(in Guid desktopId, out nint desktop); // unused placeholder

    [PreserveSig]
    int GetDesktopSwitchIncludeExcludeViews(nint desktop, out nint unknown1, out nint unknown2); // unused placeholder

    [PreserveSig]
    int SetDesktopName(nint desktop, nint name); // unused placeholder

    [PreserveSig]
    int SetDesktopWallpaper(nint desktop, nint path); // unused placeholder

    [PreserveSig]
    int UpdateWallpaperPathForAllDesktops(nint path); // unused placeholder

    [PreserveSig]
    int CopyDesktopState(nint view0, nint view1); // unused placeholder

    [PreserveSig]
    int CreateRemoteDesktop(nint path, out nint desktop); // unused placeholder

    [PreserveSig]
    int SwitchRemoteDesktop(nint desktop, nint switchType); // unused placeholder

    [PreserveSig]
    int SwitchDesktopWithAnimation(IVirtualDesktop desktop);

    [PreserveSig]
    int GetLastActiveDesktop(out nint desktop); // unused placeholder

    [PreserveSig]
    int WaitForAnimationToComplete();
}

/// <summary>Public, documented IVirtualDesktopManager (shobjidl_core.h). Stable across builds;
/// no interop risk.</summary>
[GeneratedComInterface]
[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
internal partial interface IVirtualDesktopManager
{
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);

    [PreserveSig]
    int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);

    [PreserveSig]
    int MoveWindowToDesktop(nint topLevelWindow, in Guid desktopId); // unused
}
