using System.Globalization;
using DesktopTiler.Core.Layouts;

namespace DesktopTiler.Core.Settings;

/// <summary>
/// Pure string-to-value parsing for the extension's persisted settings (see
/// DesktopTiler/Helpers/SettingsManager.cs). Kept here, free of the Command Palette Toolkit and any
/// I/O, so it's trivially unit-testable and reusable. Every method falls back to a documented
/// default for anything that isn't exactly one of the values its corresponding ChoiceSetSetting
/// offers - unknown, garbage, or empty/whitespace - and never throws: a hand-edited or stale
/// settings.json must not crash the extension.
/// </summary>
public static class SettingsParsing
{
    /// <summary>Default for <see cref="ParseLayoutKind"/> - also <c>LayoutCycle</c>'s parameterless-
    /// constructor default, so tests that don't care about settings keep behaving the same way.</summary>
    public const LayoutKind DefaultLayoutKind = LayoutKind.MasterStack;

    /// <summary>Default for <see cref="ParseMasterRatioPercent"/>, matching the "55%" choice.</summary>
    public const int DefaultMasterRatioPercent = 55;

    /// <summary>Default for <see cref="ParseGap"/>, matching the "8 px" choice.</summary>
    public const int DefaultGap = 8;

    private static readonly int[] ValidMasterRatioPercents = [40, 45, 50, 55, 60, 65, 70];

    private static readonly int[] ValidGaps = [0, 4, 8, 12, 16];

    /// <summary>Parses the "Default layout" ChoiceSetSetting's stored value - a <see cref="LayoutKind"/>
    /// enum name, e.g. "Grid" - back into the enum. Matching is exact and case-sensitive against
    /// <see cref="LayoutKind"/>'s current members (via <see cref="object.ToString"/>, not
    /// <see cref="Enum.TryParse{TEnum}(string?, out TEnum)"/>, so a numeric string like "2" doesn't
    /// silently resolve to a member either) - anything else, including <see langword="null"/> or
    /// empty, falls back to <see cref="DefaultLayoutKind"/>.</summary>
    public static LayoutKind ParseLayoutKind(string? raw)
    {
        if (!string.IsNullOrEmpty(raw))
        {
            foreach (var kind in Enum.GetValues<LayoutKind>())
            {
                if (string.Equals(kind.ToString(), raw, StringComparison.Ordinal))
                {
                    return kind;
                }
            }
        }

        return DefaultLayoutKind;
    }

    /// <summary>Parses the "Master ratio" ChoiceSetSetting's stored value (an integer percent, e.g.
    /// "55") back into an int, falling back to <see cref="DefaultMasterRatioPercent"/> unless it's
    /// exactly one of the fixed choices offered (40/45/50/55/60/65/70) - including anything
    /// numerically valid but out of that range, e.g. "10" or "200".</summary>
    public static int ParseMasterRatioPercent(string? raw) =>
        TryParseInvariantInt(raw, out var percent) && Array.IndexOf(ValidMasterRatioPercents, percent) >= 0
            ? percent
            : DefaultMasterRatioPercent;

    /// <summary>Parses the "Gap" ChoiceSetSetting's stored value (an integer pixel count, e.g. "8")
    /// back into an int, falling back to <see cref="DefaultGap"/> unless it's exactly one of the
    /// fixed choices offered (0/4/8/12/16).</summary>
    public static int ParseGap(string? raw) =>
        TryParseInvariantInt(raw, out var gap) && Array.IndexOf(ValidGaps, gap) >= 0
            ? gap
            : DefaultGap;

    private static bool TryParseInvariantInt(string? raw, out int value) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
