using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DesktopTiler.Core.Rules;
using DesktopTiler.Core.VirtualDesktops;
using DesktopTiler.Core.Windows;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// Saves the open windows as a workspace, and opens a workspace: each app that's already running
/// has its windows moved to the workspace's desktop; each app that isn't is started, and its new
/// windows are moved there as they appear (<see cref="WindowRuleEnforcer.PlaceNextWindows"/>).
/// The user stays on the current desktop throughout.
/// </summary>
internal sealed class WorkspaceLauncher
{
    private readonly VdComClient _vdClient;
    private readonly WorkspaceStore _store;
    private readonly WindowRuleEnforcer _enforcer;

    public WorkspaceLauncher(VdComClient vdClient, WorkspaceStore store, WindowRuleEnforcer enforcer)
    {
        _vdClient = vdClient;
        _store = store;
        _enforcer = enforcer;
    }

    /// <summary>Saves every open app window, with its desktop, as a new workspace.</summary>
    public CommandResult SaveCurrentLayout(bool keepOpen)
    {
        var (windows, ids) = AppWindows();
        var desktops = _vdClient.GetDesktopIdsAsync().WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
        var snapshot = windows.Select((w, i) => new SnapshotWindow(WindowEnumerator.GetImagePath(w.ProcessId), ids[i]));
        var entries = WorkspaceSnapshot.FromWindows(snapshot, desktops, out var skippedStoreApps);
        if (entries.Count == 0)
        {
            return Toast("No apps to save: open some windows first", keepOpen);
        }

        var name = WorkspaceSnapshot.DefaultName(_store.Workspaces.Select(w => w.Name));
        _store.Add(new Workspace(Guid.NewGuid(), name, entries));

        var message = $"Saved {Count(entries.Count, "app")} as \"{name}\"";
        if (skippedStoreApps > 0)
        {
            message += $" ({Count(skippedStoreApps, "Store app")} left out)";
        }

        return Toast(message, keepOpen);
    }

    /// <summary>Opens <paramref name="workspaceId"/> and reports what happened in a toast.</summary>
    public CommandResult Open(Guid workspaceId, bool keepOpen)
    {
        if (_store.Find(workspaceId) is not { } workspace)
        {
            return Toast("That workspace no longer exists", keepOpen);
        }

        var desktops = _vdClient.GetDesktopIdsAsync().WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout).ToArray();
        var (windows, ids) = AppWindows();
        var paths = windows.Select(w => WindowEnumerator.GetImagePath(w.ProcessId)).ToArray();

        int started = 0, moved = 0, inPlace = 0, missing = 0, noDesktop = 0, failed = 0;
        foreach (var entry in workspace.Entries)
        {
            if (!desktops.Contains(entry.DesktopId))
            {
                noDesktop++;
                continue;
            }

            var running = Enumerable.Range(0, windows.Count)
                .Where(i => string.Equals(WindowRules.AppName(paths[i]), entry.App, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (running.Length > 0)
            {
                var movedAny = false;
                foreach (var i in running)
                {
                    if (WindowRules.MoveTarget(entry.DesktopId, ids[i], desktops) is not { } target)
                    {
                        continue;
                    }

                    try
                    {
                        _vdClient.MoveWindowAsync(windows[i].Handle, target).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
                        movedAny = true;
                    }
                    catch (Exception ex)
                    {
                        SpikeLog.WriteLine($"Workspace {workspace.Name}: moving {entry.App} failed: {ex.Message}");
                    }
                }

                if (movedAny)
                {
                    moved++;
                }
                else
                {
                    inPlace++;
                }

                continue;
            }

            if (!File.Exists(entry.ImagePath))
            {
                missing++;
                continue;
            }

            try
            {
                _enforcer.PlaceNextWindows(entry.App, entry.DesktopId);
                Start(entry.ImagePath);
                started++;
            }
            catch (Exception ex)
            {
                failed++;
                SpikeLog.WriteLine($"Workspace {workspace.Name}: starting {entry.ImagePath} failed: {ex.Message}");
            }
        }

        var parts = new List<string>();
        Add(parts, started, "started");
        Add(parts, moved, "moved");
        Add(parts, inPlace, "already in place");
        Add(parts, missing, "not found");
        Add(parts, noDesktop, "skipped (desktop deleted)");
        Add(parts, failed, "failed to start");
        return Toast(parts.Count == 0 ? $"{workspace.Name}: no apps" : $"{workspace.Name}: {string.Join(", ", parts)}", keepOpen);
    }

    /// <summary>Starts the app through Explorer rather than as our own child process. A child of
    /// this (packaged) extension would belong to its package, and Windows ends a package's
    /// processes when it updates or stops it - which would close the user's apps along with us.</summary>
    private static void Start(string path)
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        using var process = Process.Start(new ProcessStartInfo(explorer, $"\"{path}\"") { UseShellExecute = false });
    }

    private static void Add(List<string> parts, int count, string what)
    {
        if (count > 0)
        {
            parts.Add($"{count} {what}");
        }
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static CommandResult Toast(string message, bool keepOpen) => CommandResult.ShowToast(new ToastArgs
    {
        Message = message,
        Result = keepOpen ? CommandResult.KeepOpen() : CommandResult.Dismiss(),
    });

    /// <summary>The app windows (front first) and each one's desktop id.</summary>
    private (IReadOnlyList<WindowDescriptor> Windows, IReadOnlyList<Guid> Ids) AppWindows()
    {
        var ownPid = Environment.ProcessId;
        var windows = WindowEnumerator.EnumerateZOrder(null)
            .Where(w => DesktopOccupancy.IsAppWindow(w, ownPid, WindowEnumerator.CmdPalImageName))
            .ToArray();
        var ids = _vdClient.GetWindowDesktopIdsAsync(windows.Select(w => w.Handle).ToArray())
            .WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
        return (windows, ids);
    }
}
