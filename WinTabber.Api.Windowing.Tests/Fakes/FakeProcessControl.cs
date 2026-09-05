using WinTabber.Interop;

namespace WinTabber.Api.Windowing.Tests.Fakes;

/// <summary>
/// Hand-rolled fake for the two interfaces <see cref="Suspension.ProcessSuspensionService"/>
/// depends on. Every member is implemented for real (no <see cref="NotSupportedException"/>
/// stubs) since the suspension domain is exactly what this interface pair was split out to serve.
/// </summary>
public sealed class FakeProcessControl : IProcessControl, IWindowVisibility
{
    public Dictionary<int, string> ImagePaths { get; } = new();
    public HashSet<int> HiddenHandles { get; } = [];
    public List<int> RestoredHandles { get; } = [];
    public List<int> SuspendedProcessPids { get; } = [];
    public List<int> ResumedProcessPids { get; } = [];
    public List<int> SuspendedThreadPids { get; } = [];
    public List<int> ResumedThreadPids { get; } = [];

    public Exception? ThrowOnSuspendProcess { get; set; }
    public Exception? ThrowOnSuspendProcessThreads { get; set; }

    public void SuspendProcess(int pid)
    {
        if (ThrowOnSuspendProcess is not null)
            throw ThrowOnSuspendProcess;
        SuspendedProcessPids.Add(pid);
    }

    public void ResumeProcess(int pid) => ResumedProcessPids.Add(pid);

    public void SuspendProcessThreads(int pid)
    {
        if (ThrowOnSuspendProcessThreads is not null)
            throw ThrowOnSuspendProcessThreads;
        SuspendedThreadPids.Add(pid);
    }

    public void ResumeProcessThreads(int pid) => ResumedThreadPids.Add(pid);

    public void HideWindow(int handle) => HiddenHandles.Add(handle);

    public void RestoreWindow(int handle)
    {
        HiddenHandles.Remove(handle);
        RestoredHandles.Add(handle);
    }

    public string GetProcessImagePath(int pid)
    {
        if (ImagePaths.TryGetValue(pid, out string? path))
            return path;
        throw new InvalidOperationException($"No image path configured for pid {pid}.");
    }

    public void EnableDebugPrivilege() { }
}
