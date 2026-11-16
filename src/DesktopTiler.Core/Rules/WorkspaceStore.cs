using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DesktopTiler.Core.Rules;

/// <summary>
/// The workspaces, kept in memory and in <c>workspaces.json</c> next to the extension's settings.
/// Same approach as <see cref="WindowRuleStore"/>: hand-written JSON (trim/AOT-safe), atomic
/// writes, and a missing or unreadable file means no workspaces. Safe to use from any thread.
/// </summary>
public sealed class WorkspaceStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private IReadOnlyList<Workspace> _workspaces;

    public WorkspaceStore(string path)
    {
        _path = path;
        _workspaces = Load(path);
    }

    /// <summary>Failure diagnostics hook, wired to SpikeLog by the app.</summary>
    public static Action<string>? Log { get; set; }

    /// <summary>Raised after the workspaces change (on the thread that changed them).</summary>
    public event EventHandler? Changed;

    /// <summary>The current workspaces, in creation order (a snapshot; never mutated).</summary>
    public IReadOnlyList<Workspace> Workspaces
    {
        get
        {
            lock (_gate)
            {
                return _workspaces;
            }
        }
    }

    public Workspace? Find(Guid id) => Workspaces.FirstOrDefault(w => w.Id == id);

    public void Add(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Update(all => [.. all.Where(w => w.Id != workspace.Id), workspace]);
    }

    public void Remove(Guid id) => Update(all => [.. all.Where(w => w.Id != id)]);

    public void Rename(Guid id, string name) => Change(id, w => w with { Name = name });

    /// <summary>Points the workspace's entry for <paramref name="app"/> at another desktop.</summary>
    public void SetEntryDesktop(Guid id, string app, Guid desktopId) =>
        Change(id, w => w with { Entries = [.. w.Entries.Select(e => SameApp(e, app) ? e with { DesktopId = desktopId } : e)] });

    /// <summary>Sets the arguments <paramref name="app"/> is started with (trimmed; empty for none).</summary>
    public void SetEntryArguments(Guid id, string app, string arguments) =>
        Change(id, w => w with { Entries = [.. w.Entries.Select(e => SameApp(e, app) ? e with { Arguments = arguments.Trim() } : e)] });

    public void RemoveEntry(Guid id, string app) =>
        Change(id, w => w with { Entries = [.. w.Entries.Where(e => !SameApp(e, app))] });

    /// <summary>Parses the file's content; malformed entries are skipped. Exposed for tests.</summary>
    public static IReadOnlyList<Workspace> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("workspaces", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<Workspace>();
        foreach (var w in array.EnumerateArray())
        {
            if (w.ValueKind != JsonValueKind.Object
                || !TryGetString(w, "id", out var idText) || !Guid.TryParse(idText, out var id)
                || !TryGetString(w, "name", out var name) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var entries = new List<WorkspaceEntry>();
            if (w.TryGetProperty("apps", out var apps) && apps.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in apps.EnumerateArray())
                {
                    if (e.ValueKind == JsonValueKind.Object
                        && TryGetString(e, "path", out var path) && !string.IsNullOrWhiteSpace(path)
                        && TryGetString(e, "desktop", out var desktopText) && Guid.TryParse(desktopText, out var desktop))
                    {
                        // "args" is optional: files written before it existed have none.
                        var args = TryGetString(e, "args", out var a) ? a.Trim() : string.Empty;
                        entries.Add(new WorkspaceEntry(path, desktop, args));
                    }
                }
            }

            result.Add(new Workspace(id, name, entries));
        }

        return result;
    }

    /// <summary>The file content for <paramref name="workspaces"/>. Exposed for tests.</summary>
    public static string Serialize(IReadOnlyList<Workspace> workspaces)
    {
        ArgumentNullException.ThrowIfNull(workspaces);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteStartArray("workspaces");
            foreach (var ws in workspaces)
            {
                w.WriteStartObject();
                w.WriteString("id", ws.Id.ToString("D"));
                w.WriteString("name", ws.Name);
                w.WriteStartArray("apps");
                foreach (var e in ws.Entries)
                {
                    w.WriteStartObject();
                    w.WriteString("path", e.ImagePath);
                    w.WriteString("desktop", e.DesktopId.ToString("D"));
                    if (e.Arguments.Length > 0)
                    {
                        w.WriteString("args", e.Arguments);
                    }

                    w.WriteEndObject();
                }

                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool TryGetString(JsonElement e, string name, out string value)
    {
        if (e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
        {
            value = p.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool SameApp(WorkspaceEntry entry, string app) => string.Equals(entry.App, app, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<Workspace> Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log?.Invoke($"WorkspaceStore: reading {path} failed: {ex.Message}");
            return [];
        }
    }

    private void Change(Guid id, Func<Workspace, Workspace> change) =>
        Update(all => [.. all.Select(w => w.Id == id ? change(w) : w)]);

    private void Update(Func<IReadOnlyList<Workspace>, IReadOnlyList<Workspace>> change)
    {
        lock (_gate)
        {
            var next = change(_workspaces);
            AtomicFile.WriteAllText(_path, Serialize(next));
            _workspaces = next;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
