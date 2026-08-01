using System.Diagnostics;

namespace WinTabber.Interop;
public interface IInteropProxy
{
    void BringWindowToFront(int handle);
    IEnumerable<int> EnumerateProcessWindowHandles(Process process);
    void ForceForeground(int hWnd);
    Process? GetForegroundProcess();
    Process? GetWindowProcess(int handle);
    int GetWindowProcessId(int handle);

    string GetWindowTitle(int hWnd);
    void MaximizeWindow(int handle);
    void MinimizeWindow(int handle);
    //public Icon GetWindowIcon(int handle);
    //Hook HookForegroundChangeEvent();

    public int GetForegroundWindowHandle();

    /// <summary>
    /// Activates the live preview
    /// </summary>
    /// <param name="targetWindow">the window to show by making all other windows transparent</param>
    /// <param name="windowToSpare">the window which should not be transparent but is not the target window</param>
    public void ActivateLivePreview(IntPtr targetWindow, IntPtr windowToSpare);

    /// <summary>
    /// Deactivates the live preview
    /// </summary>
    public void DeactivateLivePreview();
    WindowPlacement.WindowState GetWindowState(int handle);
    WindowPlacement GetWindowPlacement(int handle);
    void SetWindowText(int handle, string title);
    IObservable<ActiveWindowChangeData> ActiveWindowChangedEvents();
    string GetClassName(int handle);
    void MoveWindow(int handle, Point point);
    bool IsTopLevel(int handle);
    WindowStyles GetWindowStyles(int handle);
    bool IsWindowVisible(int handle);
    bool IsProcessElevated(Process process);
    void SendInput(ushort key, bool down);

    /// <summary>Suspends all threads of the process atomically (NtSuspendProcess).</summary>
    void SuspendProcess(int pid);

    /// <summary>Resumes all threads of the process atomically (NtResumeProcess).</summary>
    void ResumeProcess(int pid);

    /// <summary>Suspends each thread of the process individually, as PsSuspend does.</summary>
    void SuspendProcessThreads(int pid);

    /// <summary>Resumes each thread of the process individually, as PsSuspend does.</summary>
    void ResumeProcessThreads(int pid);

    /// <summary>Hides a window (ShowWindow SW_HIDE). No-op if the handle is not a window.</summary>
    void HideWindow(int handle);

    /// <summary>Restores and foregrounds a window (ShowWindow SW_RESTORE + SetForegroundWindow). No-op if the handle is not a window.</summary>
    void RestoreWindow(int handle);

    /// <summary>Full executable path of the process. Throws InvalidOperationException if it cannot be determined.</summary>
    string GetProcessImagePath(int pid);

    /// <summary>Best-effort enabling of SeDebugPrivilege for the current process. Call once at startup.</summary>
    void EnableDebugPrivilege();

    /// <summary>
    /// Sets WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW on the window's extended style so it can never take
    /// focus/activation, even from a mouse click. Clicks still reach its child controls.
    /// </summary>
    void MakeWindowNonActivating(nint handle);
}