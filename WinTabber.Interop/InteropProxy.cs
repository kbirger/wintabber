using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
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

            if(processName == "devzenv")
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
            var wp = NativeMethods.GetWindowPlacement(handle);
            var isMin = wp.showCmd == NativeMethods.ShowWindowCommands.Minimize || wp.showCmd == NativeMethods.ShowWindowCommands.ShowMinimized;
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
            var wp = NativeMethods.GetWindowPlacement(handle);
            var isMax = wp.showCmd == NativeMethods.ShowWindowCommands.Maximize || wp.showCmd == NativeMethods.ShowWindowCommands.ShowMaximized;
            if (isMax)
            {
                NativeMethods.ShowWindowAsync((IntPtr)handle, NativeMethods.ShowWindowCommands.Restore);
            }
            else
            {
                NativeMethods.ShowWindowAsync((IntPtr)handle, NativeMethods.ShowWindowCommands.ShowMaximized);
            }
        }

        public void MinimizeWindow(int handle)
        {
            NativeMethods.ShowWindowAsync((IntPtr)handle, NativeMethods.ShowWindowCommands.Minimize);
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

        public IEnumerable<int> EnumerateProcessWindowHandles(Process process)
        {
            return NativeMethods.EnumerateProcessWindowHandles(process);
        }


        public Icon GetWindowIcon(int handle)
        {
            try
            {
                IntPtr hIcon = NativeMethods.SendMessage(handle, NativeMethods.WM_GETICON, NativeMethods.ICON_SMALL2, 0);
                IntPtr hIcon2 = NativeMethods.LoadIcon(handle, NativeMethods.ICON_LARGE);

                return Icon.FromHandle(hIcon);

            }
            catch(Exception ex)
            {
                return null;
            }
        }
        public int GetForegroundWindowHandle()
        {
            return NativeMethods.GetForegroundWindow().ToInt32();
        }

        /// <summary>
        /// Activates the live preview
        /// </summary>
        /// <param name="targetWindow">the window to show by making all other windows transparent</param>
        /// <param name="windowToSpare">the window which should not be transparent but is not the target window</param>
        public void ActivateLivePreview(IntPtr targetWindow, IntPtr windowToSpare)
        {
            _ = NativeMethods.DwmpActivateLivePreview(
                    true,
                    targetWindow,
                    windowToSpare,
                    LivePreviewTrigger.Superbar,
                    IntPtr.Zero);
        }

        /// <summary>
        /// Deactivates the live preview
        /// </summary>
        public void DeactivateLivePreview()
        {
            _ = NativeMethods.DwmpActivateLivePreview(
                    false,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    LivePreviewTrigger.AltTab,
                    IntPtr.Zero);
        }

    }
}
