using System;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>
/// Thrown when the undocumented virtual-desktop COM interfaces (CLSID/IID pairs pinned to a
/// specific Windows build) are not supported by the running OS - i.e. QueryInterface /
/// CoCreateInstance / QueryService failed with E_NOINTERFACE. Callers should show a friendly
/// "unsupported Windows build" message rather than a raw COMException.
/// </summary>
public sealed class UnsupportedBuildException : Exception
{
    public UnsupportedBuildException(string message)
        : base(message)
    {
    }

    public UnsupportedBuildException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
