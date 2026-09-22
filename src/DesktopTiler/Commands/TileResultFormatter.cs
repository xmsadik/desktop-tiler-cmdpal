using System.Globalization;
using DesktopTiler.Core.Windows;

namespace DesktopTiler;

/// <summary>Shared toast text for every "Tile: *" command ("Tiled 3 windows · 1 skipped
/// (admin)") - factored out of the old single TileColumnsCommand so it can be reused by
/// TileLayoutCommand, NextLayoutCommand and RetileCommand alike.</summary>
internal static class TileResultFormatter
{
    public static string Format(TileResult result) => Format(result, prefix: null);

    /// <summary>Same formatting, with an optional prefix (e.g. the layout name for "Tile: Next
    /// layout", since that command doesn't otherwise tell the user which layout just ran)
    /// prepended as "{prefix} · {text}".</summary>
    public static string Format(TileResult result, string? prefix)
    {
        string text;
        if (result.Tiled == 0 && result.SkippedAdmin == 0 && result.SkippedOther == 0)
        {
            text = "No tileable windows found";
        }
        else
        {
            text = $"Tiled {result.Tiled.ToString(CultureInfo.InvariantCulture)} window" + (result.Tiled == 1 ? string.Empty : "s");
            if (result.SkippedAdmin > 0)
            {
                text += $" · {result.SkippedAdmin.ToString(CultureInfo.InvariantCulture)} skipped (admin)";
            }

            if (result.SkippedOther > 0)
            {
                text += $" · {result.SkippedOther.ToString(CultureInfo.InvariantCulture)} skipped";
            }
        }

        return prefix is null ? text : $"{prefix} · {text}";
    }
}
