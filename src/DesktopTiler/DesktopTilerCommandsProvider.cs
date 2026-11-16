using System;
using System.Linq;
using DesktopTiler.Core.Layouts;
using DesktopTiler.Core.Rules;
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
    private readonly DesktopHistory _desktopHistory = new();
    private readonly WindowEventWatcher _windowWatcher;
    private readonly DockLayoutProbe _dockLayout;
    private readonly LayoutOsd _osd;
    private readonly LayoutCycle _cycle;
    private readonly TileMemoryStore _tileMemory;
    private readonly DesktopsPage _desktopsPage;
    private readonly ManageDesktopsPage _manageDesktopsPage;
    private readonly RecentDesktopsPage _recentDesktopsPage;
    private readonly WindowRuleStore _ruleStore;
    private readonly WindowRuleEnforcer _ruleEnforcer;
    private readonly WindowRulesPage _windowRulesPage;
    private readonly WorkspaceStore _workspaceStore;
    private readonly WorkspaceLauncher _workspaceLauncher;
    private readonly WorkspacesPage _workspacesPage;
    private readonly AllWindowsPage _allWindowsPage;
    private readonly AllWindowsPage _allWindowsBandPage;
    private readonly ListItem _allWindowsBandItem;
    private readonly WrappedDockItem _dockBand;
    private readonly ICommandItem[] _commands;

    // _commands plus one "Open desktop workspace: …" per workspace; rebuilt when the workspaces change.
    private volatile ICommandItem[] _topLevel;

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

        // Feed "Last used desktop": every registry change (switches made any way) records the
        // now-current desktop; the first reading runs off-thread so a slow Explorer at login
        // can't hold up construction.
        _registryReader.Changed += (_, _) => ObserveCurrentDesktop();
        _ = System.Threading.Tasks.Task.Run(ObserveCurrentDesktop);

        // The default layout is read live from settings (see LayoutCycle's Func<LayoutKind>
        // constructor) rather than snapshotted here, so changing the "Default layout" setting
        // takes effect immediately for any session where nothing has been tiled yet.
        _cycle = new LayoutCycle(() => _settingsManager.DefaultLayout);
        _tileMemory = new TileMemoryStore();
        _osd = new LayoutOsd();

        _windowWatcher = new WindowEventWatcher();
        _dockLayout = new DockLayoutProbe();
        var desktopManager = new DesktopManager(_vdClient, _registryReader);
        _desktopsPage = new DesktopsPage(_vdClient, _registryReader, _settingsManager, _windowWatcher, _dockLayout);
        _manageDesktopsPage = new ManageDesktopsPage(desktopManager, _vdClient, _registryReader, _desktopsPage);

        // Subscribed after the history (above), so the history has the new current desktop when
        // the page re-reads its order.
        _recentDesktopsPage = new RecentDesktopsPage(desktopManager, _vdClient, _registryReader, _desktopsPage, _desktopHistory);

        // Window rules: kept in rules.json next to settings.json; the enforcer moves new windows.
        WindowRuleStore.Log = SpikeLog.WriteLine;
        _ruleStore = new WindowRuleStore(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SettingsManager.SettingsJsonPath())!, "rules.json"));
        _ruleEnforcer = new WindowRuleEnforcer(_vdClient, _windowWatcher, _ruleStore);
        _windowRulesPage = new WindowRulesPage(_ruleStore, desktopManager);

        // Workspaces: kept in workspaces.json beside rules.json; opening one places windows through
        // the rule enforcer.
        WorkspaceStore.Log = SpikeLog.WriteLine;
        _workspaceStore = new WorkspaceStore(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SettingsManager.SettingsJsonPath())!, "workspaces.json"));
        _workspaceLauncher = new WorkspaceLauncher(_vdClient, _workspaceStore, _ruleEnforcer, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SettingsManager.SettingsJsonPath())!, "launch"));
        _workspacesPage = new WorkspacesPage(_workspaceStore, _workspaceLauncher, desktopManager);

        // Command.Id must be non-empty or the host silently drops the band (see claude-tasks-cmdpal).
        _allWindowsPage = new AllWindowsPage(_vdClient, _registryReader, _desktopsPage, _settingsManager, _ruleStore, _windowWatcher);

        // The last button of the band opens the "All windows" list: icon plus an "All windows" subtitle,
        // which the Dock shows in default size and hides in compact size, like the desktop names. The
        // title is a zero-width space: with an empty title the Dock centres the subtitle on one line, while a
        // (blank) title keeps the two-line layout of the desktop buttons, subtitle at the bottom. Its own page
        // instance has an empty command name (see AllWindowsPage's name parameter).
        _allWindowsBandPage = new AllWindowsPage(_vdClient, _registryReader, _desktopsPage, _settingsManager, _ruleStore, _windowWatcher, name: string.Empty) { Id = "DesktopTiler.windows.dock" };
        _allWindowsBandItem = new ListItem(_allWindowsBandPage) { Title = "\u200B", Subtitle = "All windows", Icon = _allWindowsBandPage.Icon };

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
            new CommandItem(new LastDesktopCommand(_vdClient, _registryReader, _desktopHistory)),
            new CommandItem(_recentDesktopsPage) { Subtitle = "Desktops, most recently used first" },
            .. Enumerable.Range(1, 9).Select(n => new CommandItem(MoveWindowToDesktopCommand.ToNumber(_vdClient, _registryReader, n))),
            new CommandItem(MoveWindowToDesktopCommand.ByStep(_vdClient, _registryReader, +1)),
            new CommandItem(MoveWindowToDesktopCommand.ByStep(_vdClient, _registryReader, -1)),
            .. Enumerable.Range(1, 9).Select(n => new CommandItem(MoveWindowToDesktopCommand.ToNumber(_vdClient, _registryReader, n, follow: true))),
            new CommandItem(MoveWindowToDesktopCommand.ByStep(_vdClient, _registryReader, +1, follow: true)),
            new CommandItem(MoveWindowToDesktopCommand.ByStep(_vdClient, _registryReader, -1, follow: true)),
            new CommandItem(desktopManager.NewCommand(follow: false, keepOpen: false, "DesktopTiler.desktop.new")),
            new CommandItem(desktopManager.NewCommand(follow: true, keepOpen: false, "DesktopTiler.desktop.new.go")),
            new CommandItem(new RenameDesktopPage(desktopManager, null, keepOpen: false) { Id = "DesktopTiler.desktop.rename", Name = "Rename current desktop" }),
            new CommandItem(desktopManager.MoveCommand(null, -1, keepOpen: false, "DesktopTiler.desktop.moveleft", "Move current desktop left")),
            new CommandItem(desktopManager.MoveCommand(null, +1, keepOpen: false, "DesktopTiler.desktop.moveright", "Move current desktop right")),
            new CommandItem(desktopManager.DeleteCommand(null, keepOpen: false, "DesktopTiler.desktop.delete", "Delete current desktop")),
            new CommandItem(_manageDesktopsPage) { Subtitle = "Create, rename, reorder and delete desktops" },
            new CommandItem(_windowRulesPage) { Subtitle = "Apps whose new windows open on a chosen desktop" },
            new CommandItem(WorkspacesPage.SaveCommand(_workspaceLauncher, currentDesktopOnly: true, keepOpen: false, topLevel: true)),
            new CommandItem(WorkspacesPage.SaveCommand(_workspaceLauncher, currentDesktopOnly: false, keepOpen: false, topLevel: true)),
            new CommandItem(_workspacesPage) { Subtitle = "Open a saved set of apps, each on its own desktop" },
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

        _topLevel = BuildTopLevel();
        _workspaceStore.Changed += (_, _) =>
        {
            _topLevel = BuildTopLevel();
            try
            {
                RaiseItemsChanged();
            }
            catch (Exception ex)
            {
                SpikeLog.WriteLine($"Top-level refresh failed: {ex.Message}");
            }
        };
    }

    public override ICommandItem[] TopLevelCommands() => _topLevel;

    private ICommandItem[] BuildTopLevel() =>
    [
        .. _commands,
        .. _workspaceStore.Workspaces.Select(w => new CommandItem(WorkspacesPage.OpenCommand(_workspaceLauncher, w, keepOpen: false, topLevel: true))
        {
            Subtitle = "Desktop workspace",
        }),
    ];

    private void ObserveCurrentDesktop()
    {
        try
        {
            _desktopHistory.Observe(_vdClient.GetCurrentIdAsync().WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout));
        }
        catch (Exception ex)
        {
            // Unsupported build / Explorer restarting: the next change tries again.
            SpikeLog.WriteLine($"ObserveCurrentDesktop failed: {ex.Message}");
        }
    }

    private IListItem[] BandItems() => [.. _desktopsPage.GetItems(), _allWindowsBandItem];

    public override ICommandItem[]? GetDockBands() => [_dockBand];

    public override void Dispose()
    {
        _workspacesPage.Dispose();
        _ruleEnforcer.Dispose();
        _windowRulesPage.Dispose();
        _manageDesktopsPage.Dispose();
        _recentDesktopsPage.Dispose();
        _allWindowsPage.Dispose();
        _allWindowsBandPage.Dispose();
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
