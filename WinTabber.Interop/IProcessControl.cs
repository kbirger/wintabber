using System.Diagnostics;

namespace WinTabber.Interop;

public interface IProcessControl
{
    /// <summary>Suspends all threads of the process atomically (NtSuspendProcess).</summary>
    void SuspendProcess(int pid);

    /// <summary>Resumes all threads of the process atomically (NtResumeProcess).</summary>
    void ResumeProcess(int pid);

    /// <summary>Suspends each thread of the process individually, as PsSuspend does.</summary>
    void SuspendProcessThreads(int pid);

    /// <summary>Resumes each thread of the process individually, as PsSuspend does.</summary>
    void ResumeProcessThreads(int pid);

    /// <summary>Full executable path of the process. Throws InvalidOperationException if it cannot be determined.</summary>
    string GetProcessImagePath(int pid);

    /// <summary>Best-effort enabling of SeDebugPrivilege for the current process. Call once at startup.</summary>
    void EnableDebugPrivilege();
}
