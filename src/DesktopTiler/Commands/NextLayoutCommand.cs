using System;
using DesktopTiler.Core.Layouts;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>"Tile: Next layout" - stable id "DesktopTiler.tile.next". Advances through
/// <see cref="LayoutKind"/> in its declaration order (wrapping) and tiles with that layout.
/// Passes <see cref="LayoutCycle.Advance"/> itself as Tiler's kind-selector, so the read-advance-
/// record happens under Tiler's own tiling lock alongside the actual tile/window-order-memory
/// work - two overlapping invocations of this command can't both read the same "last" layout
/// before either records. The resulting layout's name is shown on the <see cref="LayoutOsd"/>
/// (see <see cref="TileResultFormatter.ToCommandResult"/>), since the user doesn't otherwise know
/// which layout just ran.</summary>
internal sealed partial class NextLayoutCommand : InvokableCommand
{
    private readonly VdComClient _vdClient;
    private readonly LayoutCycle _cycle;
    private readonly TileMemoryStore _memoryStore;
    private readonly SettingsManager _settingsManager;
    private readonly LayoutOsd _osd;

    public NextLayoutCommand(VdComClient vdClient, LayoutCycle cycle, TileMemoryStore memoryStore, SettingsManager settingsManager, LayoutOsd osd)
    {
        _vdClient = vdClient;
        _cycle = cycle;
        _memoryStore = memoryStore;
        _settingsManager = settingsManager;
        _osd = osd;
        Id = "DesktopTiler.tile.next";
        Name = "Tile: Next layout";
        Icon = new IconInfo("");
    }

    public override CommandResult Invoke()
    {
        using var timing = SpikeLog.Timed(Id);

        try
        {
            // Gap/master ratio are read from SettingsManager here, at invoke time, so a settings
            // change takes effect on the very next tile.
            var outcome = Tiler.Tile(_vdClient, _cycle.Advance, _memoryStore, gap: _settingsManager.Gap, masterRatio: _settingsManager.MasterRatio);
            return TileResultFormatter.ToCommandResult(outcome, _osd);
        }
        catch (UnsupportedBuildException)
        {
            return CommandResult.ShowToast("Unsupported Windows build (virtual desktop API)");
        }
        catch (ExplorerNotRespondingException)
        {
            return CommandResult.ShowToast("Explorer is not responding");
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"{Id} failed: {ex}");
            return CommandResult.ShowToast($"Tiling failed: {ex.Message}");
        }
    }
}
