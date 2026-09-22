namespace DesktopTiler.Core.Layouts;

/// <summary>
/// Tracks the last layout tiled this session, for "Tile: Retile" (re-run the same layout) and
/// "Tile: Next layout" (advance through <see cref="LayoutKind"/> in its declaration order,
/// wrapping around). Pure state, no Win32 - a command's Invoke can run concurrently with another
/// command or a Dock refresh, so all access to <see cref="_last"/> is behind a lock.
/// </summary>
public sealed class LayoutCycle
{
    private readonly object _gate = new();
    private readonly Func<LayoutKind> _defaultKind;
    private LayoutKind? _last;

    /// <summary>Defaults to <see cref="LayoutKind.MasterStack"/> - convenient for tests and any
    /// other caller that doesn't need a live/user-configurable default (see the
    /// <see cref="LayoutCycle(Func{LayoutKind})"/> overload, which the extension's provider uses
    /// to wire up the "Default layout" setting).</summary>
    public LayoutCycle()
        : this(static () => LayoutKind.MasterStack)
    {
    }

    /// <param name="defaultKind">Invoked fresh - never cached - each time nothing has been tiled
    /// yet this session, so a change to the underlying setting is honoured right up until the
    /// first tile. Once a layout has actually been tiled (<see cref="_last"/> is set, via
    /// <see cref="Record"/> or <see cref="Advance"/>), <see cref="Retile"/>/<see cref="Next"/>/
    /// <see cref="Advance"/> work off that instead and stop consulting this delegate at all.</param>
    public LayoutCycle(Func<LayoutKind> defaultKind)
    {
        _defaultKind = defaultKind ?? throw new ArgumentNullException(nameof(defaultKind));
    }

    public LayoutKind? Last
    {
        get
        {
            lock (_gate)
            {
                return _last;
            }
        }
    }

    /// <summary>The layout "Tile: Retile" should use: the last one tiled, or the configured
    /// default (see <see cref="LayoutCycle(Func{LayoutKind})"/>) if nothing has been tiled yet this
    /// session.</summary>
    public LayoutKind Retile()
    {
        lock (_gate)
        {
            return _last ?? _defaultKind();
        }
    }

    /// <summary>The layout "Tile: Next layout" should use: the one after <see cref="Last"/> in
    /// <see cref="LayoutKind"/> declaration order, wrapping from the last value back to the
    /// first. A null Last (nothing tiled yet) returns the configured default rather than the value
    /// after some assumed starting point. Does not record - see <see cref="Advance"/> for the
    /// atomic read-and-record version commands should actually use.</summary>
    public LayoutKind Next()
    {
        lock (_gate)
        {
            return ComputeNext(_last);
        }
    }

    /// <summary>Computes <see cref="Next"/> and <see cref="Record"/>s it in one step under the
    /// same lock, so two overlapping "Tile: Next layout" invocations can't both read the same
    /// <see cref="Last"/> before either records - which would otherwise let both advance to the
    /// same value (effectively skipping a step) instead of each advancing one step further.
    /// Callers should record-before-tile: call this first, then tile with the returned
    /// kind.</summary>
    public LayoutKind Advance()
    {
        lock (_gate)
        {
            var next = ComputeNext(_last);
            _last = next;
            return next;
        }
    }

    public void Record(LayoutKind kind)
    {
        lock (_gate)
        {
            _last = kind;
        }
    }

    private LayoutKind ComputeNext(LayoutKind? last)
    {
        if (last is not { } value)
        {
            return _defaultKind();
        }

        var values = Enum.GetValues<LayoutKind>();
        var index = Array.IndexOf(values, value);
        return values[(index + 1) % values.Length];
    }
}
