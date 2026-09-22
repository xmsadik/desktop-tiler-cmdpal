namespace DesktopTiler.Core.Windows;

/// <summary>Outcome of one tiling pass, for the toast text ("Tiled 3 windows · 1 skipped (admin)").</summary>
public readonly record struct TileResult(int Tiled, int SkippedAdmin, int SkippedOther)
{
    public static readonly TileResult Empty = new(0, 0, 0);
}
