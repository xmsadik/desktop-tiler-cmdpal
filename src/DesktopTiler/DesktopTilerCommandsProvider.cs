using System;
using DesktopTiler.Core.Layouts;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

public partial class DesktopTilerCommandsProvider : CommandProvider
{
    private readonly SettingsManager _settingsManager;
    private readonly VdComClient _vdClient;
    private readonly RegistryDesktopReader _registryReader;
    private readonly LayoutCycle _cycle;
    private readonly TileMemoryStore _tileMemory;
    private readonly DesktopsPage _desktopsPage;
    private readonly WrappedDockItem _dockBand;
    private readonly ICommandItem[] _commands;

    public DesktopTilerCommandsProvider()
    {
        DisplayName = "Desktop Tiler";
        Id = "DesktopTiler";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        RegistryDesktopReader.Log = SpikeLog.WriteLine;
        _settingsManager = new SettingsManager();

        _vdClient = new VdComClient { UseAnimation = _settingsManager.UseAnimation };
        _settingsManager.UseAnimationChanged += (_, _) => _vdClient.UseAnimation = _settingsManager.UseAnimation;

        _registryReader = new RegistryDesktopReader();

        // The default layout is read live from settings (see LayoutCycle's Func<LayoutKind>
        // constructor) rather than snapshotted here, so changing the "Default layout" setting
        // takes effect immediately for any session where nothing has been tiled yet.
        _cycle = new LayoutCycle(() => _settingsManager.DefaultLayout);
        _tileMemory = new TileMemoryStore();

        _desktopsPage = new DesktopsPage(_vdClient, _registryReader, _settingsManager);

        // Command.Id must be non-empty or the host silently drops the band (see claude-tasks-cmdpal).
        _dockBand = new WrappedDockItem(_desktopsPage.GetItems(), "DesktopTiler.dock.desktops", "Desktops");
        _desktopsPage.Refreshed += (_, _) => _dockBand.Items = _desktopsPage.GetItems();

        _commands =
        [
            new CommandItem(new DesktopNumberCommand(_vdClient, _registryReader, 1)),
            new CommandItem(new DesktopNumberCommand(_vdClient, _registryReader, 2)),
            new CommandItem(new DesktopNumberCommand(_vdClient, _registryReader, 3)),
            new CommandItem(new DesktopNumberCommand(_vdClient, _registryReader, 4)),
            new CommandItem(new DesktopNumberCommand(_vdClient, _registryReader, 5)),
            new CommandItem(new DesktopNumberCommand(_vdClient, _registryReader, 6)),
            new CommandItem(new DesktopNumberCommand(_vdClient, _registryReader, 7)),
            new CommandItem(new DesktopNumberCommand(_vdClient, _registryReader, 8)),
            new CommandItem(new DesktopNumberCommand(_vdClient, _registryReader, 9)),
            new CommandItem(new StepDesktopCommand(_vdClient, _registryReader, +1)),
            new CommandItem(new StepDesktopCommand(_vdClient, _registryReader, -1)),
            new CommandItem(new TileLayoutCommand(_vdClient, _cycle, _tileMemory, _settingsManager, LayoutKind.MasterStack, "DesktopTiler.tile.masterstack", "Tile: Master-stack")),
            new CommandItem(new TileLayoutCommand(_vdClient, _cycle, _tileMemory, _settingsManager, LayoutKind.Columns, "DesktopTiler.tile.columns", "Tile: Columns")),
            new CommandItem(new TileLayoutCommand(_vdClient, _cycle, _tileMemory, _settingsManager, LayoutKind.Grid, "DesktopTiler.tile.grid", "Tile: Grid")),
            new CommandItem(new TileLayoutCommand(_vdClient, _cycle, _tileMemory, _settingsManager, LayoutKind.Monocle, "DesktopTiler.tile.monocle", "Tile: Monocle")),
            new CommandItem(new TileLayoutCommand(_vdClient, _cycle, _tileMemory, _settingsManager, LayoutKind.CenterMaster, "DesktopTiler.tile.centermaster", "Tile: Center-master")),
            new CommandItem(new NextLayoutCommand(_vdClient, _cycle, _tileMemory, _settingsManager)),
            new CommandItem(new RetileCommand(_vdClient, _cycle, _tileMemory, _settingsManager)),
            new CommandItem(new RotateCommand(_vdClient, _cycle, _tileMemory, _settingsManager)),
            new CommandItem(_settingsManager.Settings.SettingsPage) { Title = "Desktop Tiler settings" },
        ];

        Settings = _settingsManager.Settings;
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => [_dockBand];

    public override void Dispose()
    {
        _desktopsPage.Dispose();
        _registryReader.Dispose();
        _vdClient.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
