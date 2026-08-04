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

    /// <summary>True if <paramref name="handle"/> still identifies a live window.</summary>
    bool IsWindow(int handle);

    /// <summary>
    /// Captures the window's current placement (via <see cref="GetWindowPlacement"/>) and moves it to a
    /// screen-space rectangle guaranteed to be outside every monitor's bounds, keeping its size unchanged.
    /// The window is not hidden (no SW_HIDE/SW_SHOW change) so DWM keeps compositing it and thumbnail
    /// previews keep rendering live. Returns the captured placement so the caller can restore it later.
    /// </summary>
    WindowPlacement MoveWindowOffScreen(int handle);

    /// <summary>Moves the window back to the given previously-captured placement. No-op if the handle is not a window.</summary>
    void RestoreWindowPosition(int handle, WindowPlacement placement);

    /// <summary>
    /// Changes only the window's size (its position, including its off-screen thumbnail position, is left
    /// alone). No-op if the handle is not a window.
    /// </summary>
    void ResizeWindow(int handle, int width, int height);

    /// <summary>
    /// Hides the window's taskbar button (sets WS_EX_TOOLWINDOW, clears WS_EX_APPWINDOW) and returns the
    /// original extended style so it can be restored later via <see cref="RestoreExtendedStyle"/>. Only
    /// call this while the window is positioned off-screen: forcing Explorer to notice the taskbar change
    /// requires a brief hide/show cycle, which would otherwise be a visible flicker. No-op (returns 0) if
    /// the handle is not a window.
    /// </summary>
    int HideFromTaskbar(int handle);

    /// <summary>Restores a previously-captured extended style (see <see cref="HideFromTaskbar"/>). Same off-screen-only caveat applies. No-op if the handle is not a window.</summary>
    void RestoreExtendedStyle(int handle, int originalExStyle);
}