using System;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>One virtual desktop as read from the registry: its id, its 0-based position in
/// <c>VirtualDesktopIDs</c> (which defines display order), and its optional user-assigned name.</summary>
public readonly record struct DesktopInfo(Guid Id, int Index, string? Name);
