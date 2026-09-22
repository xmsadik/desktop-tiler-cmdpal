using DesktopTiler.Core.Layouts;

namespace DesktopTiler.Core.Windows;

/// <summary>One window's move instruction for <see cref="WindowMover"/>: the target rect is the
/// desired *visible* frame (DWM extended frame bounds), not the raw window rect - the mover
/// measures and compensates for the invisible resize border itself.</summary>
public readonly record struct WindowPlacement(nint Handle, bool IsHung, bool HasThickFrame, bool IsMaximized, Rect TargetRect);
