using System.Diagnostics;
using System.Drawing;

namespace WinTabber.Interop;

public interface IWindowInterop : IWindowVisibility
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

    /// <summary>
    /// Sets WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW on the window's extended style so it can never take
    /// focus/activation, even from a mouse click. Clicks still reach its child controls.
    /// </summary>
    void MakeWindowNonActivating(nint handle);

    /// <summary>True if <paramref name="handle"/> still identifies a live window.</summary>
    bool IsWindow(int handle);

    /// <summary>
    /// Asks the window to close (posts WM_CLOSE), the same request a click on its X button or Alt+F4
    /// sends. The window's own message loop decides whether to close immediately, prompt to save, or
    /// ignore the request. No-op if the handle is not a window.
    /// </summary>
    void CloseWindow(int handle);

    /// <summary>
    /// Closes windows belonging to an elevated process, which <see cref="CloseWindow"/> cannot
    /// reach — Windows' UIPI blocks WM_CLOSE from this (non-elevated) process to a higher-integrity
    /// window. Launches the <c>WinTabber.Elevator</c> helper elevated (one UAC prompt) with all of
    /// <paramref name="handles"/> batched into a single invocation; the helper posts WM_CLOSE to
    /// each from an elevated context and exits. Fire-and-forget: does not wait for the helper to
    /// exit. If the UAC prompt is declined, or the helper can't be launched at all, this silently
    /// does nothing — matching how <c>WindowRef.MoveTo</c> already treats elevated windows it
    /// can't touch.
    /// </summary>
    void CloseElevatedWindows(IEnumerable<int> handles);
}
