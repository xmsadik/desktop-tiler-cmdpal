using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DesktopTiler.Core.Rules;

/// <summary>
/// The window rules, kept in memory and in a small JSON file (<c>rules.json</c> next to the
/// extension's settings). Read and written with <see cref="JsonDocument"/>/<see cref="Utf8JsonWriter"/>
/// directly - no reflection-based serialisation, so it stays trim/AOT-safe. A missing or unreadable
/// file means no rules (logged), never a crash. Safe to use from any thread.
/// </summary>
public sealed class WindowRuleStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private IReadOnlyList<WindowRule> _rules;

    public WindowRuleStore(string path)
    {
        _path = path;
        _rules = Load(path);
    }

    /// <summary>Failure diagnostics hook, wired to SpikeLog by the app (Core can't reference it).</summary>
    public static Action<string>? Log { get; set; }

    /// <summary>Raised after the rules change (on the thread that changed them).</summary>
    public event EventHandler? Changed;

    /// <summary>The current rules (a snapshot; never mutated afterwards).</summary>
    public IReadOnlyList<WindowRule> Rules
    {
        get
        {
            lock (_gate)
            {
                return _rules;
            }
        }
    }

    /// <summary>Adds the rule, replacing any existing rule for the same app.</summary>
    public void Set(WindowRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        Update(rules => [.. rules.Where(r => !SameApp(r, rule.App)), rule]);
    }

    /// <summary>Removes the rule for <paramref name="app"/>, if there is one.</summary>
    public void Remove(string app) => Update(rules => [.. rules.Where(r => !SameApp(r, app))]);

    /// <summary>Parses the file's content; malformed entries are skipped. Exposed for tests.</summary>
    public static IReadOnlyList<WindowRule> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("rules", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rules = new List<WindowRule>();
        foreach (var e in array.EnumerateArray())
        {
            if (e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("app", out var app) && app.ValueKind == JsonValueKind.String
                && e.TryGetProperty("desktop", out var desktop) && desktop.ValueKind == JsonValueKind.String
                && Guid.TryParse(desktop.GetString(), out var id)
                && !string.IsNullOrWhiteSpace(app.GetString()))
            {
                var path = e.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty;
                rules.Add(new WindowRule(app.GetString()!, id, path));
            }
        }

        return rules;
    }

    /// <summary>The file content for <paramref name="rules"/>. Exposed for tests.</summary>
    public static string Serialize(IReadOnlyList<WindowRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteStartArray("rules");
            foreach (var r in rules)
            {
                w.WriteStartObject();
                w.WriteString("app", r.App);
                w.WriteString("desktop", r.DesktopId.ToString("D"));
                w.WriteString("path", r.ImagePath);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool SameApp(WindowRule rule, string app) => string.Equals(rule.App, app, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<WindowRule> Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log?.Invoke($"WindowRuleStore: reading {path} failed: {ex.Message}");
            return [];
        }
    }

    private void Update(Func<IReadOnlyList<WindowRule>, IReadOnlyList<WindowRule>> change)
    {
        lock (_gate)
        {
            var next = change(_rules);

            // Write to a temp file, then swap it in, so a crash mid-write can't leave a torn file.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, Serialize(next));
            File.Move(temp, _path, overwrite: true);
            _rules = next;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
