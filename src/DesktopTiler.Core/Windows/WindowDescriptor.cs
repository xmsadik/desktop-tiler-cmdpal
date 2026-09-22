namespace DesktopTiler.Core.Windows;

/// <summary>
/// Everything <see cref="TargetWindowSelector"/> needs to know about one top-level window, so
/// the selection logic can run as a pure function over synthetic data in tests without any
/// live HWNDs.
/// </summary>
public readonly record struct WindowDescriptor(
    nint Handle,
    int ProcessId,
    string ImageName,
    string ClassName,
    bool IsVisible,
    bool IsToolWindow,
    bool HasOwner,
    bool IsMinimized,
    bool IsCloaked,
    bool IsHung,
    bool HasThickFrame,
    bool HasTitle,
    bool IsOnCurrentVirtualDesktop,
    nint MonitorHandle);
