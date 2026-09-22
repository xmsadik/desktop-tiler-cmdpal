using System;
using System.Diagnostics;
using System.IO;

namespace DesktopTiler;

/// <summary>
/// Phase 1 spike instrumentation: appends timing/lifecycle lines to
/// <c>%LOCALAPPDATA%\DesktopTiler\spike.log</c> so risks like whether the Dock band keeps updating
/// after the host idles the extension out (#50367), and command cold-start latency, can be
/// observed after the fact. Disabled by default (<see cref="Enabled"/>) - flip it on locally to
/// diagnose a specific issue; <see cref="RotateIfTooLarge"/> caps the file at
/// <see cref="MaxLogSizeBytes"/> so leaving it on can't grow it unbounded.
/// </summary>
internal static partial class SpikeLog
{
    private const bool Enabled = false;

    private const long MaxLogSizeBytes = 1024 * 1024; // 1 MB

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopTiler",
        "spike.log");

    private static readonly object Gate = new();

    static SpikeLog()
    {
        WriteLine($"Process started at {Process.GetCurrentProcess().StartTime:O}");
    }

    public static void WriteLine(string message)
    {
#pragma warning disable CS0162 // Enabled is a const so this can flip on for a local diagnostic build.
        if (Enabled)
        {
            TryAppend(message);
        }
#pragma warning restore CS0162
    }

    private static void TryAppend(string message)
    {
        try
        {
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (dir is not null)
                {
                    Directory.CreateDirectory(dir);
                }

                RotateIfTooLarge();
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Spike logging must never break the extension.
        }
    }

    /// <summary>Keeps spike.log bounded even if <see cref="Enabled"/> is ever flipped back on for a
    /// long-running host process: once it reaches <see cref="MaxLogSizeBytes"/>, it's rotated to
    /// <c>spike.log.old</c> (overwriting any previous one) and a fresh spike.log started, instead
    /// of growing without limit. Called under <see cref="Gate"/>, same as the append itself.</summary>
    private static void RotateIfTooLarge()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < MaxLogSizeBytes)
        {
            return;
        }

        File.Copy(LogPath, LogPath + ".old", overwrite: true);
        File.Delete(LogPath);
    }

    /// <summary>Wraps one command invocation: logs "begin" immediately and "end" (with elapsed
    /// ms) when the returned scope is disposed.</summary>
    public static IDisposable Timed(string commandId)
    {
        WriteLine($"Invoke begin: {commandId}");
        return new Scope(commandId, Stopwatch.StartNew());
    }

    private sealed partial class Scope(string commandId, Stopwatch stopwatch) : IDisposable
    {
        public void Dispose()
        {
            stopwatch.Stop();
            WriteLine($"Invoke end: {commandId} elapsedMs={stopwatch.ElapsedMilliseconds}");
        }
    }
}
