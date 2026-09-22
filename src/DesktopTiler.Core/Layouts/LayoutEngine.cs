using System;

namespace DesktopTiler.Core.Layouts;

/// <summary>
/// Single dispatch point from a <see cref="LayoutKind"/> to the per-layout static Compute method,
/// so callers (<c>Tiler</c>, tests) don't need a switch of their own. <paramref name="masterRatio"/>
/// is clamped to [0.1, 0.9] here - in addition to MasterStack/CenterMaster's own defensive clamp -
/// so an out-of-range value from, say, a future settings UI can never invert or degenerate the
/// split.
/// </summary>
public static class LayoutEngine
{
    public static Rect[] Compute(LayoutKind kind, Rect workArea, int n, int gap, double masterRatio)
    {
        if (n <= 0)
        {
            return [];
        }

        masterRatio = Math.Clamp(masterRatio, 0.1, 0.9);

        return kind switch
        {
            LayoutKind.MasterStack => MasterStackLayout.Compute(workArea, n, gap, masterRatio),
            LayoutKind.Columns => ColumnsLayout.Compute(workArea, n, gap),
            LayoutKind.Grid => GridLayout.Compute(workArea, n, gap),
            LayoutKind.Monocle => MonocleLayout.Compute(workArea, n),
            LayoutKind.CenterMaster => CenterMasterLayout.Compute(workArea, n, gap, masterRatio),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown layout kind"),
        };
    }
}
