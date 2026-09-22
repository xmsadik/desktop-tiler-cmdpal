namespace DesktopTiler.Core.Layouts;

/// <summary>
/// The tiling layouts DesktopTiler supports. Declaration order IS the cycle order that
/// "Tile: Next layout" (<see cref="LayoutCycle.Next"/>) advances through, wrapping from the last
/// value back to the first - do not reorder these without checking that command's tests.
/// </summary>
public enum LayoutKind
{
    MasterStack,
    Columns,
    Grid,
    Monocle,
    CenterMaster,
}
