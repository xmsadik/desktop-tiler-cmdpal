namespace DesktopTiler.Core.Windows;

/// <summary>
/// Keeps windows on the desktop being left from flashing in the taskbar when switching virtual
/// desktops. If the foreground window is still a window on the old desktop when the switch
/// happens, Explorer can't activate the top window of the new desktop and flashes it instead,
/// and a flashing window shows up in the taskbar on every desktop. Handing the foreground to the
/// shell's desktop window (Progman, present on every desktop) before the switch and minimizing
/// it afterwards lets Windows activate the new desktop's top window normally. This is the same
/// technique MScholtes' VirtualDesktop tool uses, and it only uses documented user32 calls.
/// </summary>
internal static class ForegroundParking
{
    /// <summary>Gives the foreground to the shell window. Returns that window, or 0 if it
    /// couldn't take the foreground (e.g. this process lacks the foreground right); in that case
    /// <see cref="Release"/> does nothing and the switch proceeds as before.</summary>
    internal static nint Park()
    {
        var shell = NativeMethods.GetShellWindow();
        return shell != 0 && NativeMethods.SetForegroundWindow(shell) ? shell : 0;
    }

    /// <summary>After the switch: if the shell window still has the foreground, minimizing it
    /// passes the focus to the topmost window of the now-current desktop.</summary>
    internal static void Release(nint shell)
    {
        if (shell != 0 && NativeMethods.GetForegroundWindow() == shell)
        {
            NativeMethods.ShowWindow(shell, NativeMethods.SwMinimize);
        }
    }
}
