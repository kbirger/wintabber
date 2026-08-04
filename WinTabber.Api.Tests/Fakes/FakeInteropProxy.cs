using System.Diagnostics;
using System.Drawing;
using WinTabber.Interop;

namespace WinTabber.Api.Tests.Fakes;

/// <summary>
/// Hand-rolled fake for <see cref="IInteropProxy"/>. Only the members exercised by
/// <c>WinTabber.API.Suspension</c> are implemented; everything else throws
/// <see cref="NotSupportedException"/> so accidental usage fails loudly.
/// </summary>
public sealed class FakeInteropProxy : IInteropProxy
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

    // ── Unused by the suspension domain layer ───────────────────────────────

    public void BringWindowToFront(int handle) => throw new NotSupportedException();

    public IEnumerable<int> EnumerateProcessWindowHandles(Process process) => throw new NotSupportedException();

    public void ForceForeground(int hWnd) => throw new NotSupportedException();

    public Process? GetForegroundProcess() => throw new NotSupportedException();

    public Process? GetWindowProcess(int handle) => throw new NotSupportedException();

    public int GetWindowProcessId(int handle) => throw new NotSupportedException();

    public string GetWindowTitle(int hWnd) => throw new NotSupportedException();

    public void MaximizeWindow(int handle) => throw new NotSupportedException();

    public void MinimizeWindow(int handle) => throw new NotSupportedException();

    public int GetForegroundWindowHandle() => throw new NotSupportedException();

    public void ActivateLivePreview(IntPtr targetWindow, IntPtr windowToSpare) => throw new NotSupportedException();

    public void DeactivateLivePreview() => throw new NotSupportedException();

    public WindowPlacement.WindowState GetWindowState(int handle) => throw new NotSupportedException();

    public WindowPlacement GetWindowPlacement(int handle) => throw new NotSupportedException();

    public void SetWindowText(int handle, string title) => throw new NotSupportedException();

    public IObservable<ActiveWindowChangeData> ActiveWindowChangedEvents() => throw new NotSupportedException();

    public string GetClassName(int handle) => throw new NotSupportedException();

    public void MoveWindow(int handle, Point point) => throw new NotSupportedException();

    public bool IsTopLevel(int handle) => throw new NotSupportedException();

    public WindowStyles GetWindowStyles(int handle) => throw new NotSupportedException();

    public bool IsWindowVisible(int handle) => throw new NotSupportedException();

    public bool IsProcessElevated(Process process) => throw new NotSupportedException();

    public void SendInput(ushort key, bool down) => throw new NotSupportedException();

    public void MakeWindowNonActivating(nint handle) => throw new NotSupportedException();

    public bool IsWindow(int handle) => throw new NotSupportedException();

    public WindowPlacement MoveWindowOffScreen(int handle) => throw new NotSupportedException();

    public void RestoreWindowPosition(int handle, WindowPlacement placement) => throw new NotSupportedException();

    public void ResizeWindow(int handle, int width, int height) => throw new NotSupportedException();

    public int HideFromTaskbar(int handle) => throw new NotSupportedException();

    public void RestoreExtendedStyle(int handle, int originalExStyle) => throw new NotSupportedException();
}
