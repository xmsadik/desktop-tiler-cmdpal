using DesktopTiler.Core.Layouts;

namespace DesktopTiler.Core.Windows;

/// <summary>Small public wrapper so the extension project can get a monitor's work area (rcWork
/// - excludes the taskbar and any AppBars, including the Command Palette's own Dock) and check
/// whether a window is maximized, without depending on <see cref="NativeMethods"/> directly.</summary>
public static class MonitorWorkArea
{
    public static Rect Get(nint monitorHandle)
    {
        var info = default(MonitorInfoNative);
        info.CbSize = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfoNative>();
        return NativeMethods.GetMonitorInfo(monitorHandle, ref info) ? info.RcWork : default;
    }

    public static bool IsZoomed(nint hwnd) => NativeMethods.IsZoomed(hwnd);
}
