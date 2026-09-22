using System;
using System.Collections.Generic;

namespace DesktopTiler.Core.Layouts;

/// <summary>
/// Latest <see cref="TileMemory"/> per (virtual desktop, monitor), shared across every tile
/// command the same way <see cref="LayoutCycle"/> shares the last layout kind (see
/// DesktopTilerCommandsProvider, which owns one instance of each). Scoped per desktop so
/// switching away and back to a desktop restores *that* desktop's remembered window order
/// instead of whatever was last tiled anywhere. Capped at <see cref="MaxEntries"/> distinct
/// (desktop, monitor) keys - dropping the oldest - so a long-running session that visits many
/// desktops/monitors doesn't grow this unboundedly.
///
/// Thread-safe (lock) on its own, but Tiler additionally reads/resolves/applies/records under
/// its own lock so the whole sequence for one tiling pass is atomic - two overlapping tile
/// commands can't interleave and produce an inconsistent memory.
/// </summary>
public sealed class TileMemoryStore
{
    private const int MaxEntries = 32;

    private readonly object _gate = new();
    private readonly Dictionary<(Guid Desktop, nint Monitor), TileMemory> _byKey = new();
    private readonly Queue<(Guid Desktop, nint Monitor)> _insertionOrder = new();

    public TileMemory? Get(Guid desktop, nint monitor)
    {
        lock (_gate)
        {
            return _byKey.TryGetValue((desktop, monitor), out var memory) ? memory : null;
        }
    }

    public void Record(Guid desktop, nint monitor, TileMemory memory)
    {
        lock (_gate)
        {
            var key = (desktop, monitor);
            if (!_byKey.ContainsKey(key))
            {
                _insertionOrder.Enqueue(key);
                while (_insertionOrder.Count > MaxEntries)
                {
                    _byKey.Remove(_insertionOrder.Dequeue());
                }
            }

            _byKey[key] = memory;
        }
    }
}
