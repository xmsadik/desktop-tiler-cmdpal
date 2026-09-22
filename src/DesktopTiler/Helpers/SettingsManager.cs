using System;
using System.IO;
using DesktopTiler.Core.Layouts;
using DesktopTiler.Core.Settings;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// Persisted extension settings, surfaced on the Command Palette settings page for Desktop Tiler
/// (see DesktopTilerCommandsProvider's "Desktop Tiler settings" command item). Turning each
/// ChoiceSetSetting's raw string value into its real type is delegated to the pure, unit-tested
/// <see cref="SettingsParsing"/> (DesktopTiler.Core/Settings/SettingsParsing.cs) so a hand-edited or
/// stale settings.json - unknown/garbage/empty values - can never crash the extension: every
/// getter here just falls back to that parser's documented default.
/// </summary>
internal sealed partial class SettingsManager : JsonSettingsManager
{
    private static readonly string _namespace = "DesktopTiler";

    private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";

    private readonly ChoiceSetSetting _defaultLayout = new(
        Namespaced(nameof(DefaultLayout)),
        "Default layout",
        "The layout \"Tile: Retile\" and \"Tile: Next layout\" start from before anything has been tiled this session",
        [
            new ChoiceSetSetting.Choice("Master-stack", nameof(LayoutKind.MasterStack)),
            new ChoiceSetSetting.Choice("Columns", nameof(LayoutKind.Columns)),
            new ChoiceSetSetting.Choice("Grid", nameof(LayoutKind.Grid)),
            new ChoiceSetSetting.Choice("Monocle", nameof(LayoutKind.Monocle)),
            new ChoiceSetSetting.Choice("Center-master", nameof(LayoutKind.CenterMaster)),
        ]);

    private readonly ChoiceSetSetting _masterRatio = new(
        Namespaced(nameof(MasterRatioPercent)),
        "Master ratio",
        "How much of the work area the master pane takes up (Master-stack and Center-master)",
        [
            new ChoiceSetSetting.Choice("40%", "40"),
            new ChoiceSetSetting.Choice("45%", "45"),
            new ChoiceSetSetting.Choice("50%", "50"),
            new ChoiceSetSetting.Choice("55%", "55"),
            new ChoiceSetSetting.Choice("60%", "60"),
            new ChoiceSetSetting.Choice("65%", "65"),
            new ChoiceSetSetting.Choice("70%", "70"),
        ]);

    private readonly ChoiceSetSetting _gap = new(
        Namespaced(nameof(Gap)),
        "Gap",
        "Space left between tiled windows, and between them and the work area's edge",
        [
            new ChoiceSetSetting.Choice("0 px", "0"),
            new ChoiceSetSetting.Choice("4 px", "4"),
            new ChoiceSetSetting.Choice("8 px", "8"),
            new ChoiceSetSetting.Choice("12 px", "12"),
            new ChoiceSetSetting.Choice("16 px", "16"),
        ]);

    private readonly ToggleSetting _switchAnimation = new(
        Namespaced(nameof(UseAnimation)),
        "Switch animation",
        "Animate when switching desktops",
        true);

    private readonly ToggleSetting _showDesktopNames = new(
        Namespaced(nameof(ShowDesktopNames)),
        "Show desktop names",
        "Show desktop names in the Dock band",
        true);

    // The values as of the last SettingsChanged firing, so the combined event (which doesn't say
    // which setting changed) can be split into UseAnimationChanged/ShowDesktopNamesChanged. The
    // other three settings (DefaultLayout/MasterRatioPercent/Gap) are read fresh at invoke time by
    // the tile commands rather than pushed to a listener, so they need no change event.
    private bool _lastUseAnimation;
    private bool _lastShowDesktopNames;

    /// <summary>Raised when "Switch animation" changes, so <c>VdComClient.UseAnimation</c> can be
    /// kept in sync.</summary>
    public event EventHandler? UseAnimationChanged;

    /// <summary>Raised when "Show desktop names" changes, so the Dock band's Desktops page can
    /// refresh immediately instead of waiting for the next registry-driven refresh.</summary>
    public event EventHandler? ShowDesktopNamesChanged;

    public LayoutKind DefaultLayout => SettingsParsing.ParseLayoutKind(_defaultLayout.Value);

    public int MasterRatioPercent => SettingsParsing.ParseMasterRatioPercent(_masterRatio.Value);

    /// <summary><see cref="MasterRatioPercent"/> as a 0..1 fraction, ready to pass straight into
    /// <c>Tiler.Tile</c>'s masterRatio parameter.</summary>
    public double MasterRatio => MasterRatioPercent / 100.0;

    public int Gap => SettingsParsing.ParseGap(_gap.Value);

    public bool UseAnimation => _switchAnimation.Value;

    public bool ShowDesktopNames => _showDesktopNames.Value;

    internal static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath("DesktopTiler");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, "settings.json");
    }

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_defaultLayout);
        Settings.Add(_masterRatio);
        Settings.Add(_gap);
        Settings.Add(_switchAnimation);
        Settings.Add(_showDesktopNames);

        // ChoiceSetSetting initialises Value to its FIRST choice, not to our default - so without
        // this a fresh install (no settings.json, or the key absent) would get 40% / 0 px. Seed
        // the real defaults before LoadSettings() overrides them with any persisted values.
        _masterRatio.Value = SettingsParsing.DefaultMasterRatioPercent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _gap.Value = SettingsParsing.DefaultGap.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Load settings from file upon initialization
        LoadSettings();

        _lastUseAnimation = UseAnimation;
        _lastShowDesktopNames = ShowDesktopNames;

        // The toolkit fires one combined event for any setting change (it doesn't say which), so
        // which of our two events to raise is determined by comparing against the values we saw
        // last time.
        Settings.SettingsChanged += (_, _) =>
        {
            try
            {
                SaveSettings();
            }
            catch (Exception)
            {
                // Persisting to disk failed (e.g. a locked/readonly settings.json); the in-memory
                // values are still correct, so the change events below must still fire rather than
                // being skipped.
            }

            var useAnimationChanged = _lastUseAnimation != UseAnimation;
            var showDesktopNamesChanged = _lastShowDesktopNames != ShowDesktopNames;

            _lastUseAnimation = UseAnimation;
            _lastShowDesktopNames = ShowDesktopNames;

            if (useAnimationChanged)
            {
                UseAnimationChanged?.Invoke(this, EventArgs.Empty);
            }

            if (showDesktopNamesChanged)
            {
                ShowDesktopNamesChanged?.Invoke(this, EventArgs.Empty);
            }
        };
    }
}
