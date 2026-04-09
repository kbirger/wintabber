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
}