using System.Diagnostics;

namespace WinTabber.Interop
{
    public interface IInteropProxy
    {
        void BringWindowToFront(int handle);
        IEnumerable<int> EnumerateProcessWindowHandles(int processId);
        void ForceForeground(int hWnd);
        Process GetForegroundProcess();
        Process GetWindowProcess(int handle);
        string GetWindowTitle(int hWnd);
        void MaximizeWindow(int handle);
        void MinimizeWindow(int handle);
    }
}