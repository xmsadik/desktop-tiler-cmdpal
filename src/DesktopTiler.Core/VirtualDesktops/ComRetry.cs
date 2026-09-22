using System;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>
/// The "reconnect once, retry once" policy for a single VD COM call, extracted out of
/// <see cref="VdComClient"/> so it can be unit tested without real COM objects or threads.
/// Per call (not per client lifetime): a transient failure triggers exactly one
/// <paramref name="reconnect"/> plus one retry of <paramref name="op"/>; if that retry also fails,
/// or the original failure wasn't transient, the exception propagates as-is.
/// </summary>
public static class ComRetry
{
    public static T Run<T>(Func<T> op, Action reconnect, Func<Exception, bool> isTransient)
    {
        ArgumentNullException.ThrowIfNull(op);
        ArgumentNullException.ThrowIfNull(reconnect);
        ArgumentNullException.ThrowIfNull(isTransient);

        try
        {
            return op();
        }
        catch (Exception ex) when (isTransient(ex))
        {
            reconnect();

            // Deliberately not wrapped in another try/catch: a second failure (transient or not)
            // propagates straight to the caller instead of retrying again.
            return op();
        }
    }
}
