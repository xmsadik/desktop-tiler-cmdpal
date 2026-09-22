using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using DesktopTiler.Core.VirtualDesktops;

namespace DesktopTiler.Core.Windows;

/// <summary>
/// Live (impure) enumeration of top-level windows into <see cref="WindowDescriptor"/>s, for
/// <see cref="TargetWindowSelector"/> to pick from. Kept thin and free of any tiling decision
/// logic, which lives entirely in the pure <see cref="TargetWindowSelector"/>.
/// </summary>
public static class WindowEnumerator
{
    /// <summary>The Command Palette host process image name, excluded from tiling consideration
    /// per the Phase 1 design.</summary>
    public const string CmdPalImageName = "Microsoft.CmdPal.UI.exe";

    /// <summary>All top-level windows, front (foreground-most) to back - the order
    /// <c>EnumWindows</c> returns them in, which matches Z-order.</summary>
    public static IReadOnlyList<WindowDescriptor> EnumerateZOrder(Func<nint, bool> isOnCurrentVirtualDesktop)
    {
        var handles = new List<nint>();
        NativeMethods.EnumWindows(
            (h, _) =>
            {
                handles.Add(h);
                return true;
            },
            0);

        var result = new List<WindowDescriptor>(handles.Count);
        foreach (var h in handles)
        {
            result.Add(Describe(h, isOnCurrentVirtualDesktop));
        }

        return result;
    }

    public static WindowDescriptor? DescribeForeground(Func<nint, bool> isOnCurrentVirtualDesktop)
    {
        var h = NativeMethods.GetForegroundWindow();
        return h == 0 ? null : Describe(h, isOnCurrentVirtualDesktop);
    }

    private static WindowDescriptor Describe(nint h, Func<nint, bool> isOnCurrentVirtualDesktop)
    {
        NativeMethods.GetWindowThreadProcessId(h, out var pid);
        var exStyle = NativeMethods.GetWindowLong(h, NativeMethods.GwlExStyle);
        var style = NativeMethods.GetWindowLong(h, NativeMethods.GwlStyle);

        return new WindowDescriptor(
            Handle: h,
            ProcessId: pid,
            ImageName: TryGetImageName(pid),
            ClassName: TryGetClassName(h),
            IsVisible: NativeMethods.IsWindowVisible(h),
            IsToolWindow: (exStyle & NativeMethods.WsExToolWindow) != 0,
            HasOwner: NativeMethods.GetWindow(h, NativeMethods.GwOwner) != 0,
            IsMinimized: NativeMethods.IsIconic(h),
            IsCloaked: IsCloaked(h),
            IsHung: NativeMethods.IsHungAppWindow(h),
            HasThickFrame: (style & NativeMethods.WsThickFrame) != 0,
            HasTitle: NativeMethods.GetWindowTextLengthW(h) > 0,
            IsOnCurrentVirtualDesktop: SafeIsOnCurrentDesktop(h, isOnCurrentVirtualDesktop),
            MonitorHandle: NativeMethods.MonitorFromWindow(h, NativeMethods.MonitorDefaultToNearest));
    }

    private static bool SafeIsOnCurrentDesktop(nint h, Func<nint, bool> isOnCurrentVirtualDesktop)
    {
        try
        {
            return isOnCurrentVirtualDesktop(h);
        }
        catch (UnsupportedBuildException)
        {
            // Not a per-window problem - the whole VD COM layer is unusable on this Windows
            // build. Swallowing it here would just make every window silently look like it's
            // not on the current desktop; let it propagate so the caller shows the intended
            // "unsupported Windows build" message instead.
            throw;
        }
        catch (ExplorerNotRespondingException)
        {
            // Same rationale as UnsupportedBuildException above, and just as not-per-window:
            // Explorer is wedged, every remaining window would fail-fast the same way (see
            // VdComClient's per-call timeout), and building a "candidate set" that silently
            // excludes whichever windows happened to be checked after the hang started would let
            // a tile command run against the wrong set of windows. Stop enumerating and let it
            // propagate so the caller shows "Explorer is not responding" instead.
            throw;
        }
        catch (Exception)
        {
            // A window that vanished mid-enumeration, or a transient COM error, shouldn't crash
            // the whole tiling attempt - just exclude it.
            return false;
        }
    }

    private static bool IsCloaked(nint h)
    {
        var hr = NativeMethods.DwmGetWindowAttribute(h, NativeMethods.DwmwaCloaked, out int cloaked, sizeof(int));
        return hr == 0 && cloaked != 0;
    }

    private static string TryGetClassName(nint h)
    {
        const int bufferChars = 256;
        var buffer = Marshal.AllocHGlobal(bufferChars * 2);
        try
        {
            var len = NativeMethods.GetClassNameRaw(h, buffer, bufferChars);
            return len > 0 ? Marshal.PtrToStringUni(buffer, len) : string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string TryGetImageName(int pid)
    {
        if (pid == 0)
        {
            return string.Empty;
        }

        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (handle == 0)
        {
            // Elevated/protected process and we're not - can't read its image name. That's
            // exactly the "admin window" case the tiler needs to skip later.
            return string.Empty;
        }

        try
        {
            const int bufferChars = 260;
            var buffer = Marshal.AllocHGlobal(bufferChars * 2);
            try
            {
                var size = bufferChars;
                if (!NativeMethods.QueryFullProcessImageNameRaw(handle, 0, buffer, ref size))
                {
                    return string.Empty;
                }

                var fullPath = Marshal.PtrToStringUni(buffer, size);
                return Path.GetFileName(fullPath);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
