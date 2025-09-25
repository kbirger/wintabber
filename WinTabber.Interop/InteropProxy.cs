using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.Interop
{
    public class InteropProxy : IInteropProxy
    {
        public void BringWindowToFront(int handle)
        {
            var process = GetWindowProcess(handle);
            var processName = process.ProcessName;

            if(processName == "devenv")
            {
                SwitchToDevenv(process, handle);
            }
            else if(UacHelper.IsProcessElevated(process.Id))
            {
                SwitchToWindowElevated(process, handle);
            }
            else
            {
                SwitchToWindowRegular(process, handle);
            }
        }

        private void SwitchToWindowRegular(Process pid, int handle)
        {
            var hWnd = NativeMethods.GetForegroundWindow();

            var didSet = NativeMethods.SetForegroundWindow(handle);
            var isMin = NativeMethods.GetWindowPlacement(handle).showCmd == NativeMethods.ShowWindowCommands.Minimize;
            var newFgWindow = NativeMethods.GetForegroundWindow();

            if (newFgWindow != handle)
            {
                NativeMethods.ShowWindowAsync(handle, NativeMethods.ShowWindowCommands.Show);
            }
            if (isMin)
            {
                NativeMethods.ShowWindowAsync(handle, NativeMethods.ShowWindowCommands.Restore);
            }
        }

        private void SwitchToWindowElevated(Process pid, int handle)
        {
            NativeMethods.ShowWindowAsync(handle, NativeMethods.ShowWindowCommands.Restore);
            NativeMethods.SendMessage(handle, NativeMethods.WM_SYSCOMMAND, NativeMethods.SystemCommand.Restore);
        }

        private void SwitchToDevenv(Process pid, int handle)
        {
            ForceForeground(handle);
        }

        public string GetWindowTitle(int hWnd)
        {
            int nChars = NativeMethods.GetWindowTextLength(hWnd);
            StringBuilder Buff = new StringBuilder(nChars + 1);
            if (NativeMethods.GetWindowText(hWnd, Buff, nChars + 1) > 0)
            {
                return Buff.ToString();
            }
            return String.Empty;
        }

        public void MaximizeWindow(int handle)
        {
            throw new NotImplementedException();
        }

        public void MinimizeWindow(int handle)
        {
            throw new NotImplementedException();
        }

        public Process GetForegroundProcess()
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return Process.GetProcessById((int)pid);
        }

        public Process GetWindowProcess(int handle)
        {
            NativeMethods.GetWindowThreadProcessId((IntPtr)handle, out uint pid);
            return Process.GetProcessById((int)pid);
        }

        public void ForceForeground(int hWnd)
        {
            // From: https://stackoverflow.com/a/5694425/1889329
            IntPtr foreHwnd = NativeMethods.GetForegroundWindow();
            uint foreThread = NativeMethods.GetWindowThreadProcessId(foreHwnd, out _);
            uint appThread = NativeMethods.GetCurrentThreadId();
            // attach threads to get around restrictions
            if (foreThread != appThread)
            {
                NativeMethods.AttachThreadInput(foreThread, appThread, true);
                NativeMethods.BringWindowToTop((IntPtr)hWnd); // IE 5.0 related hack
                //NativeMethods.ShowWindowAsync((IntPtr)hWnd, NativeMethods.ShowWindowCommands.Show);
                NativeMethods.SetForegroundWindow((IntPtr)hWnd);
                NativeMethods.AttachThreadInput(foreThread, appThread, false);
            }
            else
            {
                NativeMethods.BringWindowToTop((IntPtr)hWnd); // IE 5.0 related hack
                NativeMethods.ShowWindowAsync((IntPtr)hWnd, NativeMethods.ShowWindowCommands.Show);
                NativeMethods.SetForegroundWindow((IntPtr)hWnd);
            }
        }

        public IEnumerable<int> EnumerateProcessWindowHandles(int processId)
        {
            return NativeMethods.EnumerateProcessWindowHandles(processId);
        }
    }
}
