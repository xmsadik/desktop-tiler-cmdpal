using System;
using System.Runtime.InteropServices;
using DesktopTiler.Core.Layouts;

namespace DesktopTiler.Core.Windows;

/// <summary>
/// user32/dwmapi/kernel32 P/Invoke surface for window enumeration and tiling, all via
/// [LibraryImport] (trim/AOT-safe, no classic DllImport marshalling). String-returning APIs
/// (GetClassName, QueryFullProcessImageName) take a raw unmanaged buffer pointer rather than a
/// StringBuilder/char[], which LibraryImport can't marshal without a custom marshaller.
/// </summary>
internal static partial class NativeMethods
{
    internal const int GwlStyle = -16;
    internal const int GwlExStyle = -20;
    internal const long WsThickFrame = 0x00040000;
    internal const long WsExToolWindow = 0x00000080;
    internal const uint GwOwner = 4;
    internal const uint MonitorDefaultToNearest = 2;
    internal const int DwmwaCloaked = 14;
    internal const int DwmwaExtendedFrameBounds = 9;
    internal const int SwMaximize = 3;
    internal const int SwShowNoActivate = 4;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const uint SwpAsyncWindowPos = 0x4000;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint EventObjectDestroy = 0x8001;
    internal const uint EventObjectHide = 0x8003;
    internal const uint EventObjectCloaked = 0x8017;
    internal const uint EventObjectUncloaked = 0x8018;
    internal const uint WinEventOutOfContext = 0x0000;
    internal const uint WinEventSkipOwnProcess = 0x0002;
    internal const int ObjIdWindow = 0;
    internal const uint GaRoot = 2;
    internal const uint WmQuit = 0x0012;
    internal const uint PmNoRemove = 0x0000;

    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    internal static partial nint GetWindow(nint hWnd, uint uCmd);

    [LibraryImport("user32.dll")]
    internal static partial int GetWindowTextLengthW(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    internal static partial int GetClassNameRaw(nint hWnd, nint lpClassName, int nMaxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsZoomed(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsHungAppWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromWindow(nint hWnd, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hWnd, out Rect lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out Rect pvAttribute, int cbAttribute);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryFullProcessImageNameRaw(nint hProcess, uint dwFlags, nint lpExeName, ref int lpdwSize);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint hMonitor, ref MonitorInfoNative lpmi);

    [LibraryImport("user32.dll")]
    internal static unsafe partial nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> pfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWinEvent(nint hWinEventHook);

    [LibraryImport("user32.dll")]
    internal static partial nint GetAncestor(nint hwnd, uint gaFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    internal static partial int GetMessage(out MsgNative lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessage(out MsgNative lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    internal static long GetWindowLong(nint hWnd, int nIndex) => GetWindowLongPtr(hWnd, nIndex).ToInt64();
}

// Fully qualified: DesktopTiler.Core.Layouts.LayoutKind (imported above for Rect) otherwise
// collides with System.Runtime.InteropServices.LayoutKind here.
[StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
internal struct MonitorInfoNative
{
    public int CbSize;
    public Rect RcMonitor;
    public Rect RcWork;
    public uint DwFlags;
}

/// <summary>Win32 MSG. Only ever filled by GetMessage/PeekMessage; never dispatched, since the
/// WinEvent watcher's message loop exists solely to deliver out-of-context hook callbacks.</summary>
[StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
internal struct MsgNative
{
    public nint Hwnd;
    public uint Message;
    public nint WParam;
    public nint LParam;
    public uint Time;
    public int PtX;
    public int PtY;
    public uint LPrivate;
}
