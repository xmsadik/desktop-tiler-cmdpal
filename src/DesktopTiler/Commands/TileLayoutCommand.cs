using System;
using DesktopTiler.Core.Layouts;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>One "Tile: {Layout}" command per <see cref="LayoutKind"/> - stable ids, since users
/// bind hotkeys to them (see the registrations in DesktopTilerCommandsProvider; these ids MUST
/// NOT change). Tiles the current monitor's tileable windows into the given layout, once, and
/// records it in <paramref name="cycle"/> so "Tile: Retile" and "Tile: Next layout" pick up from
/// here. No continuous re-tiling - re-run the command to re-tile.</summary>
internal sealed partial class TileLayoutCommand : InvokableCommand
{
    private readonly VdComClient _vdClient;
    private readonly LayoutCycle _cycle;
    private readonly TileMemoryStore _memoryStore;
    private readonly SettingsManager _settingsManager;
    private readonly LayoutOsd _osd;
    private readonly LayoutKind _kind;

    public TileLayoutCommand(VdComClient vdClient, LayoutCycle cycle, TileMemoryStore memoryStore, SettingsManager settingsManager, LayoutOsd osd, LayoutKind kind, string id, string name)
    {
        _vdClient = vdClient;
        _cycle = cycle;
        _memoryStore = memoryStore;
        _settingsManager = settingsManager;
        _osd = osd;
        _kind = kind;
        Id = id;
        Name = name;
        Icon = new IconInfo("");
    }

    public override CommandResult Invoke()
    {
        using var timing = SpikeLog.Timed(Id);

        try
        {
            // Recording happens inside the closure Tiler runs under its own tiling lock, so an
            // overlapping invocation of this command (or Next/Retile/Rotate) can never record a
            // kind other than the one it actually tiled with. Gap/master ratio are read from
            // SettingsManager here, at invoke time, rather than captured at construction, so a
            // settings change takes effect on the very next tile.
            var outcome = Tiler.Tile(
                _vdClient,
                () => { _cycle.Record(_kind); return _kind; },
                _memoryStore,
                gap: _settingsManager.Gap,
                masterRatio: _settingsManager.MasterRatio);
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
