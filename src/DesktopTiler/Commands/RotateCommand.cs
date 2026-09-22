using System;
using DesktopTiler.Core.Layouts;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>"Tile: Rotate" - stable id "DesktopTiler.tile.rotate". Re-tiles with whichever layout
/// "Tile: Retile" would use (the last one tiled this session, or the configured "Default layout"
/// setting if nothing has been tiled yet), but first rotates the remembered window order one step:
/// whatever was at index 1 becomes the new master (see <see cref="WindowOrder"/>, applied via
/// <c>Tiler.Tile</c>'s <c>rotate</c> parameter). Unlike <see cref="RetileCommand"/>, the point
/// here isn't re-running a layout - it's cycling *who's master* without picking a different one.
/// The toast is prefixed with the layout name, same as <see cref="NextLayoutCommand"/>, since the
/// user doesn't otherwise know which layout it just re-ran.</summary>
internal sealed partial class RotateCommand : InvokableCommand
{
    private readonly VdComClient _vdClient;
    private readonly LayoutCycle _cycle;
    private readonly TileMemoryStore _memoryStore;
    private readonly SettingsManager _settingsManager;

    public RotateCommand(VdComClient vdClient, LayoutCycle cycle, TileMemoryStore memoryStore, SettingsManager settingsManager)
    {
        _vdClient = vdClient;
        _cycle = cycle;
        _memoryStore = memoryStore;
        _settingsManager = settingsManager;
        Id = "DesktopTiler.tile.rotate";
        Name = "Tile: Rotate";
        Icon = new IconInfo("");
    }

    public override CommandResult Invoke()
    {
        using var timing = SpikeLog.Timed(Id);

        try
        {
            // Gap/master ratio are read from SettingsManager here, at invoke time, so a settings
            // change takes effect on the very next tile.
            var outcome = Tiler.Tile(_vdClient, SelectAndRecordKind, _memoryStore, rotate: true, gap: _settingsManager.Gap, masterRatio: _settingsManager.MasterRatio);
            return CommandResult.ShowToast(TileResultFormatter.Format(outcome.Result, LayoutDisplayNames.For(outcome.Kind)));
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
