using System.Globalization;
using DesktopTiler.Core.Windows;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>Shared toast text for every "Tile: *" command ("Tiled 3 windows · 1 skipped
/// (admin)") - factored out of the old single TileColumnsCommand so it can be reused by
/// TileLayoutCommand, NextLayoutCommand and RetileCommand alike.</summary>
internal static class TileResultFormatter
{
    public static string Format(TileResult result) => Format(result, prefix: null);

    /// <summary>The command result for a finished tiling pass, shared by every "Tile: *" command.
    /// If windows were actually moved, the layout name and result go on the topmost
    /// <see cref="LayoutOsd"/> (the host's toast would end up behind the windows just moved) and
    /// the palette is dismissed; otherwise - nothing tiled, so nothing covers it, or the OSD
    /// couldn't be shown - the toast is kept.</summary>
    public static CommandResult ToCommandResult(TileOutcome outcome, LayoutOsd osd)
    {
        var layoutName = LayoutDisplayNames.For(outcome.Kind);
        if (outcome.WorkArea is { } workArea
            && outcome.Result.Tiled > 0
            && osd.Show(layoutName, Format(outcome.Result), workArea))
        {
            return CommandResult.Dismiss();
        }

        return CommandResult.ShowToast(Format(outcome.Result, layoutName));
    }

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
