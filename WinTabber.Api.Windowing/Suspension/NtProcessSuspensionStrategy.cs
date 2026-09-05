using WinTabber.Interop;

namespace WinTabber.Api.Windowing.Suspension;

/// <summary>
/// Suspends/resumes an entire process atomically using NtSuspendProcess / NtResumeProcess.
/// </summary>
public sealed class NtProcessSuspensionStrategy(IProcessControl interop) : ISuspensionStrategy
{
    public string Name => "process";

    public void Suspend(int pid) => interop.SuspendProcess(pid);

    public void Resume(int pid) => interop.ResumeProcess(pid);
}
