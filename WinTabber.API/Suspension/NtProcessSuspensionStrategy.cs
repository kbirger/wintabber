using WinTabber.Interop;

namespace WinTabber.API.Suspension;

/// <summary>
/// Suspends/resumes an entire process atomically using NtSuspendProcess / NtResumeProcess.
/// </summary>
public sealed class NtProcessSuspensionStrategy(IInteropProxy interop) : ISuspensionStrategy
{
    public string Name => "process";

    public void Suspend(int pid) => interop.SuspendProcess(pid);

    public void Resume(int pid) => interop.ResumeProcess(pid);
}
