using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace DesktopTiler.Core.VirtualDesktops;

/// <summary>
/// Thrown by <see cref="TaskTimeoutExtensions"/> when a blocking wait for a VD COM call exceeds
/// its timeout - almost always because Explorer's virtual-desktop COM server is hung or hasn't
/// started yet, and <see cref="VdComClient"/>'s dedicated STA thread has no timeout of its own on
/// the native call it's blocked in. Callers should treat this the same as any other "the desktop
/// operation failed" error and show a friendly toast (or otherwise degrade gracefully) rather than
/// let the host thread that called into the extension hang indefinitely.
/// </summary>
public sealed class ExplorerNotRespondingException : Exception
{
    /// <summary>The message every call site (this class and <see cref="VdComClient"/>'s fail-fast/
    /// deadline checks) uses, so there's exactly one user-facing wording to keep in sync.</summary>
    public const string DefaultMessage = "Explorer is not responding.";

    public ExplorerNotRespondingException(string message)
        : base(message)
    {
    }

    public ExplorerNotRespondingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A bounded alternative to <c>task.GetAwaiter().GetResult()</c> for the handful of call sites
/// (host-invoked command <c>Invoke()</c>s, <c>DesktopsPage.GetItems</c>, <c>Tiler.Gather</c>) that
/// must block synchronously on a <see cref="VdComClient"/> call but can't risk blocking forever if
/// Explorer's virtual-desktop COM server is wedged - the host thread calling into the extension
/// would hang right along with it.
/// </summary>
public static class TaskTimeoutExtensions
{
    /// <summary>The timeout every call site uses. A single shared constant (rather than each call
    /// site picking its own) so "how long do we let Explorer take" is one decision, made once.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Waits up to <paramref name="timeout"/> for <paramref name="task"/> to complete and
    /// returns its result. If the task instead faults within the timeout, its original exception is
    /// rethrown unwrapped (same as <c>GetAwaiter().GetResult()</c>, not wrapped in an
    /// <see cref="AggregateException"/>). If it doesn't complete in time,
    /// <see cref="ExplorerNotRespondingException"/> is thrown instead - the underlying task is left
    /// running (it isn't cancelled) since <see cref="VdComClient"/>'s single STA thread can't safely
    /// abandon a native call mid-flight.</summary>
    public static T WaitOrTimeout<T>(this Task<T> task, TimeSpan timeout)
    {
        if (!TryWait(task, timeout))
        {
            throw new ExplorerNotRespondingException(ExplorerNotRespondingException.DefaultMessage);
        }

        return task.GetAwaiter().GetResult();
    }

    /// <summary>Non-generic counterpart of <see cref="WaitOrTimeout{T}"/> for a <see cref="Task"/>
    /// with no result.</summary>
    public static void WaitOrTimeout(this Task task, TimeSpan timeout)
    {
        if (!TryWait(task, timeout))
        {
            throw new ExplorerNotRespondingException(ExplorerNotRespondingException.DefaultMessage);
        }

        task.GetAwaiter().GetResult();
    }

    /// <summary>Returns whether <paramref name="task"/> completed within <paramref name="timeout"/>.
    /// <see cref="Task.Wait(TimeSpan)"/> itself throws an <see cref="AggregateException"/> if the
    /// task faults before the timeout elapses (rather than just returning true); that's unwrapped
    /// and rethrown here so a faulted task always surfaces its original exception type, whether it
    /// faults before or is still faulted by the time the caller checks.</summary>
    private static bool TryWait(Task task, TimeSpan timeout)
    {
        try
        {
            return task.Wait(timeout);
        }
        catch (AggregateException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw; // Unreachable - Throw() always throws.
        }
    }
}
