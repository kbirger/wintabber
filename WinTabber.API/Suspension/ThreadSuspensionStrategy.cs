using WinTabber.Interop;

namespace WinTabber.API.Suspension;

/// <summary>
/// Suspends/resumes a process by iterating its threads individually,
/// matching the approach used by PsSuspend from Sysinternals.
/// </summary>
public sealed class ThreadSuspensionStrategy(IInteropProxy interop) : ISuspensionStrategy
{
    public string Name => "thread";

    public void Suspend(int pid) => interop.SuspendProcessThreads(pid);

    public void Resume(int pid) => interop.ResumeProcessThreads(pid);
}
