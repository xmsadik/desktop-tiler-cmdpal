using System;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>
/// Connection-state policy for <see cref="VdComClient"/>'s lazy connect to the internal
/// (undocumented) virtual-desktop manager. Extracted out so it can be unit tested without real COM
/// objects or threads, same rationale as <see cref="ComRetry"/>.
///
/// Once a connect attempt fails with <see cref="UnsupportedBuildException"/>, that failure is
/// permanent for the life of the process - the running Windows build's COM layout doesn't match
/// what Phase 1 was built against, and it never will mid-session - so every later call should
/// rethrow the same cached exception (<see cref="Sticky"/>) without attempting
/// CoCreateInstance/QueryService again. Any other connect failure (e.g.
/// RPC_S_SERVER_UNAVAILABLE because Explorer isn't up yet at login) is transient and is not
/// remembered here at all, so the next call simply tries to connect again from scratch.
/// </summary>
public sealed class VdConnectionState
{
    private UnsupportedBuildException? _sticky;

    /// <summary>Whether a previous connect attempt latched a sticky <see cref="UnsupportedBuildException"/>.</summary>
    public bool IsUnsupported => _sticky is not null;

    /// <summary>The cached exception to rethrow while <see cref="IsUnsupported"/> is true.</summary>
    public UnsupportedBuildException Sticky =>
        _sticky ?? throw new InvalidOperationException($"{nameof(VdConnectionState)} has no sticky failure recorded.");

    /// <summary>Records the outcome of a failed connect attempt. Only
    /// <see cref="UnsupportedBuildException"/> latches permanently; anything else leaves the state
    /// open so the next call retries.</summary>
    public void RecordFailure(Exception ex)
    {
        if (ex is UnsupportedBuildException unsupported)
        {
            _sticky = unsupported;
        }
    }
}
