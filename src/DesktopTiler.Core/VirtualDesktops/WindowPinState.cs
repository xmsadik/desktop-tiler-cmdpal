namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>Whether a window is shown on all desktops by itself (<paramref name="Window"/>) and
/// whether its whole app is (<paramref name="App"/>).</summary>
public readonly record struct WindowPinState(bool Window, bool App);
