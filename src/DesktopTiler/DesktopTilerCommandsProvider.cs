using System;
using System.Linq;
using DesktopTiler.Core.Layouts;
using DesktopTiler.Core.VirtualDesktops;
using DesktopTiler.Core.Windows;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

public partial class DesktopTilerCommandsProvider : CommandProvider
{
    private readonly SettingsManager _settingsManager;
    private readonly VdComClient _vdClient;
    private readonly RegistryDesktopReader _registryReader;
    private readonly WindowEventWatcher _windowWatcher;
    private readonly DockLayoutProbe _dockLayout;
    private readonly LayoutOsd _osd;
    private readonly LayoutCycle _cycle;
    private readonly TileMemoryStore _tileMemory;
    private readonly DesktopsPage _desktopsPage;
    private readonly AllWindowsPage _allWindowsPage;
    private readonly ListItem _allWindowsBandItem;
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
        _osd = new LayoutOsd();

        _windowWatcher = new WindowEventWatcher();
        _dockLayout = new DockLayoutProbe();
        _desktopsPage = new DesktopsPage(_vdClient, _registryReader, _settingsManager, _windowWatcher, _dockLayout);

        // Command.Id must be non-empty or the host silently drops the band (see claude-tasks-cmdpal).
        _allWindowsPage = new AllWindowsPage(_vdClient, _registryReader, _desktopsPage, _settingsManager);

        // The last button of the band opens the "All windows" list: icon plus an "All windows" subtitle,
        // which the Dock shows in default size and hides in compact size, like the desktop names. The
        // title is a zero-width space: with an empty title the Dock centres the subtitle on one line, while a
        // (blank) title keeps the two-line layout of the desktop buttons, subtitle at the bottom. Its own page
        // instance has an empty command name (see AllWindowsPage's name parameter).
        var bandPage = new AllWindowsPage(_vdClient, _registryReader, _desktopsPage, _settingsManager, name: string.Empty) { Id = "DesktopTiler.windows.dock" };
        _allWindowsBandItem = new ListItem(bandPage) { Title = "\u200B", Subtitle = "All windows", Icon = bandPage.Icon };

        _dockBand = new WrappedDockItem(BandItems(), "DesktopTiler.dock.desktops", "Desktops");
        _desktopsPage.Refreshed += (_, _) => _dockBand.Items = BandItems();

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
            .. Enumerable.Range(1, 9).Select(n => new CommandItem(MoveWindowToDesktopCommand.ToNumber(_vdClient, _registryReader, n))),
            new CommandItem(MoveWindowToDesktopCommand.ByStep(_vdClient, _registryReader, +1)),
            new CommandItem(MoveWindowToDesktopCommand.ByStep(_vdClient, _registryReader, -1)),
            new CommandItem(new TileLayoutCommand(_vdClient, _cycle, _tileMemory, _settingsManager, _osd, LayoutKind.MasterStack, "DesktopTiler.tile.masterstack", "Tile: Master-stack")),
            new CommandItem(new TileLayoutCommand(_vdClient, _cycle, _tileMemory, _settingsManager, _osd, LayoutKind.Columns, "DesktopTiler.tile.columns", "Tile: Columns")),
            new CommandItem(new TileLayoutCommand(_vdClient, _cycle, _tileMemory, _settingsManager, _osd, LayoutKind.Grid, "DesktopTiler.tile.grid", "Tile: Grid")),
            new CommandItem(new TileLayoutCommand(_vdClient, _cycle, _tileMemory, _settingsManager, _osd, LayoutKind.Monocle, "DesktopTiler.tile.monocle", "Tile: Monocle")),
            new CommandItem(new TileLayoutCommand(_vdClient, _cycle, _tileMemory, _settingsManager, _osd, LayoutKind.CenterMaster, "DesktopTiler.tile.centermaster", "Tile: Center-master")),
            new CommandItem(new NextLayoutCommand(_vdClient, _cycle, _tileMemory, _settingsManager, _osd)),
            new CommandItem(new RetileCommand(_vdClient, _cycle, _tileMemory, _settingsManager, _osd)),
            new CommandItem(new RotateCommand(_vdClient, _cycle, _tileMemory, _settingsManager, _osd)),
            new CommandItem(_allWindowsPage) { Title = "All windows", Subtitle = "Windows on every desktop; pick one to switch to it" },
            new CommandItem(_settingsManager.Settings.SettingsPage) { Title = "Desktop Tiler settings" },
        ];

        Settings = _settingsManager.Settings;
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    private IListItem[] BandItems() => [.. _desktopsPage.GetItems(), _allWindowsBandItem];

    public override ICommandItem[]? GetDockBands() => [_dockBand];

    public override void Dispose()
    {
        _desktopsPage.Dispose();
        _windowWatcher.Dispose();
        _dockLayout.Dispose();
        _osd.Dispose();
        _registryReader.Dispose();
        _vdClient.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
