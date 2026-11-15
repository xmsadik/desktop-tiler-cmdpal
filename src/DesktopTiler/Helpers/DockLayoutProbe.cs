using System;
using System.IO;
using System.Text.Json;

namespace DesktopTiler;

/// <summary>
/// Tells whether the Dock currently hides button subtitles, by reading Command Palette's own
/// settings file. The desktop buttons carry their window list on extra subtitle lines (the only
/// way to put text in the Dock's tooltip, see DesktopWindowGroups.TooltipSubtitle); where the
/// subtitle is visible - the Default Dock size with labels on - those lines would stretch the
/// buttons, so the list is only added while subtitles are hidden (Compact size, or labels off).
///
/// This depends on Command Palette's internal settings format. Anything unexpected (file
/// missing, unreadable, keys renamed) answers "subtitles visible", which simply leaves the
/// window list out - never a broken Dock. <see cref="Changed"/> fires when the file changes, so
/// switching the Dock size takes effect without waiting for the next window event.
/// </summary>
internal sealed partial class DockLayoutProbe : IDisposable
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages",
        "Microsoft.CommandPalette_8wekyb3d8bbwe",
        "LocalState",
        "settings.json");

    private readonly object _gate = new();
    private readonly FileSystemWatcher? _watcher;
    private DateTime _cachedStamp;
    private bool _cachedHidden;

    public DockLayoutProbe()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (dir is not null && Directory.Exists(dir))
            {
                _watcher = new FileSystemWatcher(dir, Path.GetFileName(SettingsPath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                };
                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.Renamed += OnFileChanged;
                _watcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"DockLayoutProbe: can't watch {SettingsPath}: {ex.Message}");
            _watcher = null;
        }
    }

    /// <summary>Raised (on a thread-pool thread) when Command Palette's settings file changes and
    /// <see cref="SubtitlesHidden"/> may have flipped.</summary>
    public event EventHandler? Changed;

    /// <summary>True when the Dock is Compact or shows no labels, i.e. the extra subtitle lines
    /// only reach the tooltip. Re-read only when the file's timestamp changes.</summary>
    public bool SubtitlesHidden
    {
        get
        {
            lock (_gate)
            {
                try
                {
                    var stamp = File.GetLastWriteTimeUtc(SettingsPath);
                    if (stamp != _cachedStamp)
                    {
                        _cachedHidden = ReadSubtitlesHidden();
                        _cachedStamp = stamp;
                    }

                    return _cachedHidden;
                }
                catch (Exception ex)
                {
                    SpikeLog.WriteLine($"DockLayoutProbe: read failed: {ex.Message}");
                    return false;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
    }

    private static bool ReadSubtitlesHidden()
    {
        using var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("DockSettings", out var dock) || dock.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (dock.TryGetProperty("ShowLabels", out var labels) && labels.ValueKind == JsonValueKind.False)
        {
            return true;
        }

        return dock.TryGetProperty("DockSize", out var size)
            && size.ValueKind == JsonValueKind.String
            && string.Equals(size.GetString(), "Compact", StringComparison.OrdinalIgnoreCase);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"DockLayoutProbe: Changed handler failed: {ex.Message}");
        }
    }
}
