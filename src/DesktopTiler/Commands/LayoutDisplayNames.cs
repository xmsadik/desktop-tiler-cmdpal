using DesktopTiler.Core.Layouts;

namespace DesktopTiler;

/// <summary>Human-readable names for each <see cref="LayoutKind"/>, matching the "Tile: {Name}"
/// command names minus the "Tile: " prefix - reused by NextLayoutCommand's toast prefix so the
/// user can tell which layout it just cycled to.</summary>
internal static class LayoutDisplayNames
{
    public static string For(LayoutKind kind) => kind switch
    {
        LayoutKind.MasterStack => "Master-stack",
        LayoutKind.Columns => "Columns",
        LayoutKind.Grid => "Grid",
        LayoutKind.Monocle => "Monocle",
        LayoutKind.CenterMaster => "Center-master",
        _ => kind.ToString(),
    };
}
