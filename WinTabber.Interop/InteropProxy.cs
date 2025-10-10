using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinTabber.Interop
{
    public class InteropProxy : IInteropProxy
    {
        public void BringWindowToFront(int handle)
        {
            var process = GetWindowProcess(handle);
            var processName = process.ProcessName;
            var hwnd = new HWND(handle);
            if(processName == "devzenv")
            {
                SwitchToDevenv(process, handle);
            }
            else if(UacHelper.IsProcessElevated(process.Id))
            {
                SwitchToWindowElevated(process, hwnd);
            }
            else
            {
                SwitchToWindowRegular(process, hwnd);
            }
        }

        private void SwitchToWindowRegular(Process pid, HWND targetHandle)
        {
            var hWnd = PInvoke.GetForegroundWindow();

            var didSet = PInvoke.SetForegroundWindow(targetHandle);
            WINDOWPLACEMENT wp = new WINDOWPLACEMENT();
            var result = PInvoke.GetWindowPlacement(targetHandle, ref wp);
            var isMin = wp.showCmd == SHOW_WINDOW_CMD.SW_MINIMIZE || wp.showCmd == SHOW_WINDOW_CMD.SW_SHOWMINIMIZED;
            var newFgWindow = PInvoke.GetForegroundWindow();

            if (newFgWindow != targetHandle)
            {
                PInvoke.ShowWindowAsync(targetHandle, SHOW_WINDOW_CMD.SW_SHOW);
            }
            if (isMin)
            {
                PInvoke.ShowWindowAsync(targetHandle, SHOW_WINDOW_CMD.SW_RESTORE);
            }
        }

        private void SwitchToWindowElevated(Process pid, HWND handle)
        {
            PInvoke.ShowWindowAsync(handle, SHOW_WINDOW_CMD.SW_RESTORE);
            PInvoke.SendMessage(handle, PInvoke.WM_SYSCOMMAND, new WPARAM(PInvoke.SC_RESTORE), new LPARAM());
        }

        private void SwitchToDevenv(Process pid, int handle)
        {
            ForceForeground(handle);
        }

        public unsafe string GetWindowTitle(int handle)
        {
            var hwnd = new HWND(handle);
            int capacity = PInvoke.GetWindowTextLength(hwnd) + 1;
            int length;
            Span<char> buffer = capacity < 1024 ? stackalloc char[capacity] : new char[capacity];
            fixed (char* pBuffer = buffer)
            {
                length = PInvoke.GetWindowText(hwnd, pBuffer, capacity);

            }
            return buffer[..length].ToString();
        }

        public void MaximizeWindow(int handle)
        {
            var hwnd = new HWND(handle);
            var wp = new WINDOWPLACEMENT();
            var result = PInvoke.GetWindowPlacement(hwnd, ref wp);

            var isMax = wp.showCmd == SHOW_WINDOW_CMD.SW_MAXIMIZE|| wp.showCmd == SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED;
            if (isMax)
            {
                PInvoke.ShowWindowAsync(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
            }
            else
            {
                PInvoke.ShowWindowAsync(hwnd, SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED);
            }
        }

        public void MinimizeWindow(int handle)
        {
            PInvoke.ShowWindowAsync(new HWND(handle), SHOW_WINDOW_CMD.SW_MINIMIZE);
        }

        public unsafe Process GetForegroundProcess()
        {
            var hwnd = PInvoke.GetForegroundWindow();

            uint pid = 0;
            uint thread = PInvoke.GetWindowThreadProcessId(hwnd, &pid);
            if (thread != 0)
            {
                return Process.GetProcessById((int)pid);
            }
            throw new InvalidOperationException("Could not determine process");
        }

        public unsafe int GetWindowProcessId(int handle)
        {
            uint pid = 0;
            PInvoke.GetWindowThreadProcessId(new HWND(handle), &pid);
            return (int)pid;
        }
        public unsafe Process GetWindowProcess(int handle)
        {
            uint pid = 0;
            PInvoke.GetWindowThreadProcessId(new HWND(handle), &pid);
            return Process.GetProcessById((int)pid);
        }

        public unsafe void ForceForeground(int hWndPtr)
        {
            // From: https://stackoverflow.com/a/5694425/1889329
            var foreHwnd = PInvoke.GetForegroundWindow();
            uint pid = 0;
            uint foreThread = PInvoke.GetWindowThreadProcessId(foreHwnd, &pid);
            uint appThread = PInvoke.GetCurrentThreadId();
            // attach threads to get around restrictions
            var hWnd = new HWND(hWndPtr);
            if (foreThread != appThread)
            {
                PInvoke.AttachThreadInput(foreThread, appThread, true);
                PInvoke.BringWindowToTop(hWnd); // IE 5.0 related hack
                //PInvoke.ShowWindowAsync(hWnd, SHOW_WINDOW_CMD.Show);
                PInvoke.SetForegroundWindow(hWnd);
                PInvoke.AttachThreadInput(foreThread, appThread, false);
            }
            else
            {
                PInvoke.BringWindowToTop(hWnd); // IE 5.0 related hack
                PInvoke.ShowWindowAsync(hWnd, SHOW_WINDOW_CMD.SW_SHOW);
                PInvoke.SetForegroundWindow(hWnd);
            }
        }

        public IEnumerable<int> EnumerateProcessWindowHandles(Process process)
        {
            return NativeMethods.EnumerateProcessWindowHandles(process);
        }

        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);


        public Hook HookForegroundChangeEvent()
        {        
            return Hook.Create();
        }

        public class Hook : SafeHandleZeroOrMinusOneIsInvalid
        {
            private static readonly Subject<(int, long)> _subject = new Subject<(int, long)>();
            private readonly HWINEVENTHOOK _hook;
            private readonly GCHandle _procHandle;
            private bool _disposed;
            private static Hook? _instance;


            private Hook() : base(true)
            {
                WINEVENTPROC del = new WINEVENTPROC(EventProc);
                _procHandle = GCHandle.Alloc(EventProc);
                //_handler = handler;
                _hook = PInvoke.SetWinEventHook(
                    PInvoke.EVENT_SYSTEM_FOREGROUND,
                    PInvoke.EVENT_SYSTEM_FOREGROUND,
                    new HMODULE(Process.GetCurrentProcess().MainModule?.BaseAddress ?? -1),
                    EventProc,
                    0,
                    0,
                    PInvoke.WINEVENT_OUTOFCONTEXT);

                if (_hook.IsNull)
                {
                    //_procHandle.Free();
                    ThrowLastUnmanagedErrorAsException();
                }
            }

            public IObservable<(int, long)> Events
            {
                get
                {
                    Create();

                    return _subject.AsObservable();
                }
            }
            protected static void ThrowLastUnmanagedErrorAsException()
            {
                var errorCode = Marshal.GetLastWin32Error();
                throw new Win32Exception(errorCode);
            }
            private unsafe static void EventProc(HWINEVENTHOOK hWinEventHook, uint eventId, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
            {
                _subject.OnNext(((int)hwnd.Value, idChild));
            }
            internal static Hook Create()
            {
                if(_instance is null)
                {
                    _instance = new Hook();
                }

                return _instance;
            }

            public static void Release()
            {
                _instance?.Dispose();
                _instance = null;
            }
            public void Dispose()
            {

            }

            protected override bool ReleaseHandle()
            {
                if (_disposed)
                {
                    return true;
                }

                var ret = PInvoke.UnhookWinEvent(_hook);
                if (_procHandle.IsAllocated)
                {
                    _procHandle.Free();
                }
                _disposed = true;

                return ret;
            }

            ~Hook()
            {
                Dispose();
            }
        }


        //public Icon GetWindowIcon(int handle)
        //{
        //    try
        //    {
        //        IntPtr hIcon = PInvoke.SendMessage(handle, PInvoke.WM_GETICON, PInvoke.ICON_SMALL2, 0);
        //        IntPtr hIcon2 = PInvoke.LoadIcon(handle, PInvoke.ICON_LARGE);

        //        return Icon.FromHandle(hIcon);

        //    }
        //    catch(Exception ex)
        //    {
        //        return null;
        //    }
        //}
        public int GetForegroundWindowHandle()
        {
            return ((IntPtr)PInvoke.GetForegroundWindow()).ToInt32();
        }

        /// <summary>
        /// Activates the live preview
        /// </summary>
        /// <param name="targetWindow">the window to show by making all other windows transparent</param>
        /// <param name="windowToSpare">the window which should not be transparent but is not the target window</param>
        public void ActivateLivePreview(IntPtr targetWindow, IntPtr windowToSpare)
        {
            //_ = PInvoke.DwmActivateLivePreview(
            //        true,
            //        targetWindow,
            //        windowToSpare,
            //        LivePreviewTrigger.Superbar,
            //        IntPtr.Zero);
        }

        /// <summary>
        /// Deactivates the live preview
        /// </summary>
        public void DeactivateLivePreview()
        {
            //PInvoke.dwmac.DwmActivateLivePreview(
            //        false,
            //        IntPtr.Zero,
            //        IntPtr.Zero,
            //        LivePreviewTrigger.AltTab,
            //        IntPtr.Zero);
        }

    }
}
