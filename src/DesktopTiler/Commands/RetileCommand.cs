using System;
using DesktopTiler.Core.Layouts;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>"Tile: Retile" - stable id "DesktopTiler.tile.retile". Re-runs whichever layout was
/// last tiled this session (<see cref="LayoutCycle.Retile"/>), or the configured "Default layout"
/// setting if nothing has been tiled yet - useful after opening/closing a window without
/// re-picking a specific layout command. The read-then-record happens inside the closure Tiler
/// runs under its own tiling lock, atomically with the actual tile/window-order-memory work.</summary>
internal sealed partial class RetileCommand : InvokableCommand
{
    private readonly VdComClient _vdClient;
    private readonly LayoutCycle _cycle;
    private readonly TileMemoryStore _memoryStore;
    private readonly SettingsManager _settingsManager;

    public RetileCommand(VdComClient vdClient, LayoutCycle cycle, TileMemoryStore memoryStore, SettingsManager settingsManager)
    {
        _vdClient = vdClient;
        _cycle = cycle;
        _memoryStore = memoryStore;
        _settingsManager = settingsManager;
        Id = "DesktopTiler.tile.retile";
        Name = "Tile: Retile";
        Icon = new IconInfo("");
    }

    public override CommandResult Invoke()
    {
        using var timing = SpikeLog.Timed(Id);

        try
        {
            // Gap/master ratio are read from SettingsManager here, at invoke time, so a settings
            // change takes effect on the very next tile.
            var outcome = Tiler.Tile(_vdClient, SelectAndRecordKind, _memoryStore, gap: _settingsManager.Gap, masterRatio: _settingsManager.MasterRatio);
            return CommandResult.ShowToast(TileResultFormatter.Format(outcome.Result));
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

    private LayoutKind SelectAndRecordKind()
    {
        var kind = _cycle.Retile();
        _cycle.Record(kind);
        return kind;
    }
}
