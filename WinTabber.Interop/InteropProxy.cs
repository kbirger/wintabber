using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using static WinTabber.Interop.WindowPlacement;

namespace WinTabber.Interop;

public class InteropProxy : IInteropProxy
{
    public void BringWindowToFront(int handle)
    {
        var process = GetWindowProcess(handle);
        var processName = process.ProcessName;
        var hwnd = new HWND(handle);
        if (processName == "devzenv")
        {
            SwitchToDevenv(process, handle);
        }
        else if (UacHelper.IsProcessElevated(process.Id))
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

    public WindowState GetWindowState(int handle)
    {
        var hwnd = new HWND(handle);
        var wp = new WINDOWPLACEMENT();
        var result = PInvoke.GetWindowPlacement(hwnd, ref wp);


        switch(wp.showCmd)
        {
            case SHOW_WINDOW_CMD.SW_HIDE:
                return WindowState.Hidden;
            case SHOW_WINDOW_CMD.SW_SHOWNORMAL:
            case SHOW_WINDOW_CMD.SW_RESTORE:
                return WindowState.Normal;
            case SHOW_WINDOW_CMD.SW_MAXIMIZE:
                return WindowState.Maximized;
            case SHOW_WINDOW_CMD.SW_MINIMIZE:
            case SHOW_WINDOW_CMD.SW_SHOWMINIMIZED:
            case SHOW_WINDOW_CMD.SW_FORCEMINIMIZE:
                return WindowState.Minimized;
            default:
                return 0;
        };
    }

    public WindowPlacement GetWindowPlacement(int handle)
    {
        var hwnd = new HWND(handle);
        var wp = new WINDOWPLACEMENT();
        var result = PInvoke.GetWindowPlacement(hwnd, ref wp);

        var state = wp.showCmd switch
        {
            SHOW_WINDOW_CMD.SW_HIDE => WindowPlacement.WindowState.Hidden,
            _ when wp.showCmd == SHOW_WINDOW_CMD.SW_SHOWNORMAL || wp.showCmd == SHOW_WINDOW_CMD.SW_RESTORE || wp.showCmd == SHOW_WINDOW_CMD.SW_NORMAL => WindowPlacement.WindowState.Normal,
            _ when wp.showCmd == SHOW_WINDOW_CMD.SW_SHOWMINIMIZED || wp.showCmd == SHOW_WINDOW_CMD.SW_MINIMIZE => WindowPlacement.WindowState.Minimized,
            _ when wp.showCmd == SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED || wp.showCmd == SHOW_WINDOW_CMD.SW_MAXIMIZE => WindowPlacement.WindowState.Maximized
        };
        var primaryScreen = Screen.PrimaryScreen.Bounds;
        return new WindowPlacement
        {
            State = state,
            Bounds = state switch
            {
                WindowState.Maximized => new Rectangle(wp.ptMaxPosition, primaryScreen.Size),
                WindowState.Minimized => new Rectangle(wp.ptMinPosition, primaryScreen.Size),
                WindowState.Normal => wp.rcNormalPosition,
                WindowState.Hidden => wp.rcNormalPosition
            }
        };
    }

    public void MoveWindow(int handle, Point point)
    {
        var placement = GetWindowPlacement(handle);
        if(!PInvoke.MoveWindow(new HWND(handle), point.X, point.Y, placement.Bounds.Width, placement.Bounds.Height, true))
        {
            ThrowLastUnmanagedErrorAsException();
        }
    }


    public void MaximizeWindow(int handle)
    {
        var hwnd = new HWND(handle);
        var wp = new WINDOWPLACEMENT();
        var result = PInvoke.GetWindowPlacement(hwnd, ref wp);

        var isMax = wp.showCmd == SHOW_WINDOW_CMD.SW_MAXIMIZE || wp.showCmd == SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED;
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

    public unsafe Process? GetForegroundProcess()
    {
        var hwnd = PInvoke.GetForegroundWindow();

        uint pid = 0;
        uint thread = PInvoke.GetWindowThreadProcessId(hwnd, &pid);
        if (thread != 0)
        {
            return Process.GetProcessById((int)pid);
        }
        //throw new InvalidOperationException("Could not determine process");
        return null;
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
    public bool IsTopLevel(int handle)
    {
        var hwnd = new HWND(handle);
        IntPtr root = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOTOWNER);
        return root == hwnd;            
    }

    public IEnumerable<int> EnumerateProcessWindowHandles(Process process)
    {
        return NativeMethods.EnumerateProcessWindowHandles(process);
    }

    public bool IsWindowVisible(int handle)
    {
        return PInvoke.IsWindowVisible(new HWND(handle));
    }

    public WindowStyles GetWindowStyles(int handle)
    {
        var hwnd = new HWND(handle);
        var exStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        //if(exStyle == 0)
        //{
        //    ThrowLastUnmanagedErrorAsException();
        //}
        return WindowStyles.FromFlags(exStyle);
    }

    delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);


    public unsafe IObservable<ActiveWindowChangeData> ActiveWindowChangedEvents()
    {
        return Observable.Create<ActiveWindowChangeData>(observer =>
        {
            WINEVENTPROC callback = (hHook, type, hwnd, obj, idChild, threadId, time) =>
            {
                if (hwnd.IsNull)
                {
                    return;
                }
                observer.OnNext(new ActiveWindowChangeData(((nint)hwnd.Value).ToInt32(), idChild, threadId, time));
            };
            var procHandle = GCHandle.Alloc(callback);

            var hook = PInvoke.SetWinEventHook(
                PInvoke.EVENT_SYSTEM_FOREGROUND,
                PInvoke.EVENT_SYSTEM_FOREGROUND,
                new HMODULE(Process.GetCurrentProcess().MainModule?.BaseAddress ?? -1),
                callback,
                0,
                0,
                PInvoke.WINEVENT_OUTOFCONTEXT);

            if (hook.IsNull)
            {
                procHandle.Free();
            }
            return () =>
            {
                if (procHandle.IsAllocated)
                {
                    procHandle.Free();
                }
                PInvoke.UnhookWinEvent(hook);
            };
        });
    }

    public string GetClassName(int handle)
    {
        Span<char> className = Span<char>.Empty;
        if (PInvoke.GetClassName(new HWND(handle), className) != 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        ;

        return className.ToString();
    }


    public bool IsProcessElevated(Process process)
    {
        return UacHelper.IsProcessElevated(process);
        //SafeFileHandle tokenHandle = new SafeFileHandle();
        //try
        //{

        //    var processHandle = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION, false, );
        //    if (!PInvoke.OpenProcessToken(hnd,
        //                          TOKEN_ACCESS_MASK.TOKEN_QUERY, out tokenHandle))
        //    {
        //        // Handle error if token cannot be opened
        //        return false;
        //    }

        //    TOKEN_ELEVATION elevation;
        //    uint returnLength;
        //    uint elevationSize = (uint)Marshal.SizeOf(typeof(TOKEN_ELEVATION));
        //    var elevationPtr = Marshal.AllocHGlobal((int)elevationSize);
        //    try
        //    {
        //        if (PInvoke.GetTokenInformation(
        //            tokenHandle, 
        //            TOKEN_INFORMATION_CLASS.TokenElevation,
        //            elevationPtr.ToPointer(), 
        //            elevationSize, out returnLength))
        //        {
        //            elevation = Marshal.PtrToStructure<TOKEN_ELEVATION>(elevationPtr);
        //            return elevation.TokenIsElevated != 0;
        //        }
        //        else
        //        {
        //            // Handle error if token information cannot be retrieved
        //            return false;
        //        }
        //    }
        //    finally
        //    {
        //        Marshal.FreeHGlobal(elevationPtr);
        //    }
        //}
        //finally
        //{
        //    if (!tokenHandle.IsInvalid && !tokenHandle.IsClosed)
        //    {
        //        tokenHandle.Dispose();
        //    }
        //}
    }

    private static void ThrowLastUnmanagedErrorAsException()
    {
        var errorCode = Marshal.GetLastWin32Error();
        throw new Win32Exception(errorCode);
    }
    //public Hook HookForegroundChangeEvent()
    //{
    //    return Hook.Create();
    //}

    //public class Hook : SafeHandleZeroOrMinusOneIsInvalid
    //{
    //    private static readonly Subject<(int, long)> _subject = new Subject<(int, long)>();
    //    private HWINEVENTHOOK _hook;
    //    private readonly GCHandle _procHandle;
    //    private bool _disposed;
    //    private static Hook? _instance;
    //    public IObservable<(int, long)> Events { get; private set; }

    //    private unsafe Hook() : base(true)
    //    {
    //        Events = 
    //        //WINEVENTPROC del = new WINEVENTPROC(EventProc);
    //        //_procHandle = GCHandle.Alloc(EventProc);
    //        ////_handler = handler;
    //        //_hook = PInvoke.SetWinEventHook(
    //        //    PInvoke.EVENT_SYSTEM_FOREGROUND,
    //        //    PInvoke.EVENT_SYSTEM_FOREGROUND,
    //        //    new HMODULE(Process.GetCurrentProcess().MainModule?.BaseAddress ?? -1),
    //        //    EventProc,
    //        //    0,
    //        //    0,
    //        //    PInvoke.WINEVENT_OUTOFCONTEXT);

    //        //if (_hook.IsNull)
    //        //{
    //        //    //_procHandle.Free();
    //        //    ThrowLastUnmanagedErrorAsException();
    //        //}
    //    }

    //    //public IObservable<(int, long)> Events
    //    //{
    //    //    get
    //    //    {
    //    //        Create();

    //    //        return _subject.AsObservable();
    //    //    }
    //    //}
    //    protected static void ThrowLastUnmanagedErrorAsException()
    //    {
    //        var errorCode = Marshal.GetLastWin32Error();
    //        throw new Win32Exception(errorCode);
    //    }
    //    private unsafe static void EventProc(HWINEVENTHOOK hWinEventHook, uint eventId, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    //    {
    //        _subject.OnNext(((int)hwnd.Value, idChild));
    //    }
    //    internal static Hook Create()
    //    {
    //        if (_instance is null)
    //        {
    //            _instance = new Hook();
    //        }

    //        return _instance;
    //    }

    //    public static void Release()
    //    {
    //        _instance?.Dispose();
    //        _instance = null;
    //    }
    //    public void Dispose()
    //    {

    //    }

    //    protected override bool ReleaseHandle()
    //    {
    //        if (_disposed)
    //        {
    //            return true;
    //        }

    //        var ret = PInvoke.UnhookWinEvent(_hook);
    //        if (_procHandle.IsAllocated)
    //        {
    //            _procHandle.Free();
    //        }
    //        _disposed = true;

    //        return ret;
    //    }

    //    ~Hook()
    //    {
    //        Dispose();
    //    }
    //}

    //public void MoveWindow(int handle, int x, int y)
    //{
    //    var hwnd = new HWND(handle);
    //    PInvoke.GetWindowRect(hwnd, out var rect);
    //    PInvoke.MoveWindow(hwnd, x, )
    //}

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

    public void SetWindowText(int handle, string title)
    {
        var hwnd = new HWND(handle);
        var ret = PInvoke.SetWindowText(hwnd, title);

        if (!ret)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new Win32Exception(errorCode);
        }
    }

    /// <summary>
    /// Activates the live preview
    /// </summary>
    /// <param name="targetWindow">the window to show by making all other windows transparent</param>
    /// <param name="windowToSpare">the window which should not be transparent but is not the target window</param>
    public void ActivateLivePreview(IntPtr targetWindow, IntPtr windowToSpare)
    {
        _ = PInvoke.DwmpActivateLivePreview(
                true,
                targetWindow,
                windowToSpare,
                PInvoke.LivePreviewTrigger.Superbar,
                IntPtr.Zero);
    }

    /// <summary>
    /// Deactivates the live preview
    /// </summary>
    public void DeactivateLivePreview()
    {
        PInvoke.DwmpActivateLivePreview(
                false,
                IntPtr.Zero,
                IntPtr.Zero,
                PInvoke.LivePreviewTrigger.AltTab,
                IntPtr.Zero);
    }


    public void SendInput(ushort key, bool down)
    {
        PInvoke.keybd_event((byte)key, 0, down ? 0 : KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, 0);
    }

    public void SendInput2(ushort key, bool down)
    {
        INPUT input = new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous =
            {
               ki = new KEYBDINPUT
               {
                   dwFlags = down switch
                   {
                       true => 0,
                       false => KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP
                   },
                   wVk = (VIRTUAL_KEY)key,
                   dwExtraInfo = (nuint)PInvoke.GetMessageExtraInfo().Value,

               }
            }
        };
        Span<INPUT> inputs = new(ref input);
        var ret = PInvoke.SendInput(inputs, Marshal.SizeOf(typeof(INPUT)));
    }

}
