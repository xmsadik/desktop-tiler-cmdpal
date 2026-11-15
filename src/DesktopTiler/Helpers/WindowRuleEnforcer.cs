using System;
using System.Linq;
using System.Threading;
using DesktopTiler.Core.Rules;
using DesktopTiler.Core.VirtualDesktops;
using DesktopTiler.Core.Windows;

namespace DesktopTiler;

/// <summary>
/// Applies the window rules: whenever the window watcher reports a change, finds the app windows
/// that weren't open at the last look (<see cref="NewWindowTracker"/>) and moves each one whose
/// app has a rule to that rule's desktop. The user stays where they are. Windows that were
/// already open when the extension started, or when the first rule was made, are left alone, and
/// so is a window the user later moves elsewhere by hand (it isn't new any more).
///
/// Workspaces add one-off placements (<see cref="PendingPlacements"/>) through
/// <see cref="PlaceNextWindows"/> just before starting an app; a placement wins over the app's
/// rule while it lasts.
///
/// With no rules and no placements nothing is scanned at all; the tracker is reset instead, so
/// adding the first rule (or placement) starts from the windows open at that moment.
/// </summary>
internal sealed partial class WindowRuleEnforcer : IDisposable
{
    private readonly VdComClient _vdClient;
    private readonly WindowEventWatcher _watcher;
    private readonly WindowRuleStore _store;
    private readonly NewWindowTracker _tracker = new();
    private readonly PendingPlacements _placements = new();

    // Changed handlers run on the thread pool and can overlap; one pass at a time.
    private readonly object _gate = new();

    public WindowRuleEnforcer(VdComClient vdClient, WindowEventWatcher watcher, WindowRuleStore store)
    {
        _vdClient = vdClient;
        _watcher = watcher;
        _store = store;

        _watcher.Changed += OnWindowsChanged;
        _store.Changed += OnRulesChanged;

        // Record the windows already open, off-thread so a slow Explorer can't hold up startup.
        _ = System.Threading.Tasks.Task.Run(() => Enforce("startup"));
    }

    /// <summary>Makes new windows of <paramref name="app"/> (an executable file name) that open
    /// within <see cref="PendingPlacements.Lifetime"/> go to <paramref name="desktopId"/>. Call it
    /// before starting the app: it records the windows open now first (synchronously), so the
    /// app's window can't be mistaken for one that was already there.</summary>
    public void PlaceNextWindows(string app, Guid desktopId)
    {
        _placements.Add(app, desktopId, Environment.TickCount64);
        Enforce("placement added");
    }

    public void Dispose()
    {
        _watcher.Changed -= OnWindowsChanged;
        _store.Changed -= OnRulesChanged;
    }

    private void OnWindowsChanged(object? sender, EventArgs e) => Enforce("windows changed");

    // A rule set change seeds the tracker right away (first rule) so the windows open now count
    // as existing, not as new on the next window event.
    private void OnRulesChanged(object? sender, EventArgs e) => Enforce("rules changed");

    private void Enforce(string reason)
    {
        if (!Monitor.TryEnter(_gate, TaskTimeoutExtensions.DefaultTimeout))
        {
            SpikeLog.WriteLine($"WindowRuleEnforcer ({reason}): previous pass still running, skipped");
            return;
        }

        try
        {
            var rules = _store.Rules;
            var now = Environment.TickCount64;
            if (rules.Count == 0 && !_placements.Any(now))
            {
                _tracker.Reset();
                return;
            }

            var ownPid = Environment.ProcessId;
            var windows = WindowEnumerator.EnumerateZOrder(null)
                .Where(w => DesktopOccupancy.IsAppWindow(w, ownPid, WindowEnumerator.CmdPalImageName))
                .ToArray();
            var added = _tracker.Update(windows.Select(w => w.Handle)).ToHashSet();
            if (added.Count == 0)
            {
                return;
            }

            var fresh = windows.Where(w => added.Contains(w.Handle)).ToArray();
            var windowDesktops = _vdClient.GetWindowDesktopIdsAsync(fresh.Select(w => w.Handle).ToArray())
                .WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
            var desktops = _vdClient.GetDesktopIdsAsync().WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout).ToArray();

            for (var i = 0; i < fresh.Length; i++)
            {
                var path = WindowEnumerator.GetImagePath(fresh[i].ProcessId);
                var wanted = _placements.TargetFor(WindowRules.AppName(path), now) ?? WindowRules.Find(rules, path)?.DesktopId;
                if (WindowRules.MoveTarget(wanted, windowDesktops[i], desktops) is not { } target)
                {
                    continue;
                }

                try
                {
                    _vdClient.MoveWindowAsync(fresh[i].Handle, target).WaitOrTimeout(TaskTimeoutExtensions.DefaultTimeout);
                    SpikeLog.WriteLine($"WindowRuleEnforcer: moved {WindowRules.AppName(path)} window 0x{fresh[i].Handle:X} to {target}");
                }
                catch (Exception ex)
                {
                    // Typically E_ACCESSDENIED for an elevated window, or it closed meanwhile.
                    SpikeLog.WriteLine($"WindowRuleEnforcer: moving {WindowRules.AppName(path)} window failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            // Runs on the thread pool: an escaping exception would take the process down.
            SpikeLog.WriteLine($"WindowRuleEnforcer ({reason}) failed: {ex.Message}");
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }
}
