using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Threading;
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
            _ when wp.showCmd == SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED || wp.showCmd == SHOW_WINDOW_CMD.SW_MAXIMIZE => WindowPlacement.WindowState.Maximized,
            _ => throw new NotSupportedException($"Unhandled show command: {wp.showCmd}")
        };
        var primaryScreen = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).Bounds;
        return new WindowPlacement
        {
            State = state,
            Bounds = state switch
            {
                WindowState.Maximized => new Rectangle(wp.ptMaxPosition, primaryScreen.Size),
                WindowState.Minimized => new Rectangle(wp.ptMinPosition, primaryScreen.Size),
                WindowState.Normal => wp.rcNormalPosition,
                WindowState.Hidden => wp.rcNormalPosition,
                _ => throw new NotSupportedException($"Unhandled window state: {state}")
            },
            // Always the true restored geometry, independent of current show state (Windows tracks this
            // separately from Bounds above even while the window is maximized/minimized).
            NormalBounds = wp.rcNormalPosition
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

    // ── Process suspension (Phase 1 of process-suspension-plan.md) ─────────────

    public void SuspendProcess(int pid)
    {
        using var hProcess = OpenProcessForSuspendResume(pid);
        uint status = NtNativeMethods.NtSuspendProcess(hProcess.DangerousGetHandle());
        if (status != 0)
        {
            throw new InvalidOperationException($"NtSuspendProcess failed with NTSTATUS 0x{status:X8}.");
        }
    }

    public void ResumeProcess(int pid)
    {
        using var hProcess = OpenProcessForSuspendResume(pid);
        uint status = NtNativeMethods.NtResumeProcess(hProcess.DangerousGetHandle());
        if (status != 0)
        {
            throw new InvalidOperationException($"NtResumeProcess failed with NTSTATUS 0x{status:X8}.");
        }
    }

    private static SafeHandle OpenProcessForSuspendResume(int pid)
    {
        var hProcess = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_SUSPEND_RESUME, false, (uint)pid);
        if (hProcess.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Cannot open process {pid}. Win32 error {err}. Try running as Administrator.");
        }
        return hProcess;
    }

    public void SuspendProcessThreads(int pid)
    {
        ForEachThread(pid, hThread => PInvoke.SuspendThread(hThread));
    }

    public void ResumeProcessThreads(int pid)
    {
        ForEachThread(pid, hThread => PInvoke.ResumeThread(hThread));
    }

    private static void ForEachThread(int pid, Action<HANDLE> action)
    {
        using var snap = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPTHREAD, 0);
        if (snap.IsInvalid)
        {
            throw new InvalidOperationException("CreateToolhelp32Snapshot failed.");
        }

        var entry = new THREADENTRY32 { dwSize = (uint)Marshal.SizeOf<THREADENTRY32>() };

        if (!PInvoke.Thread32First(snap, ref entry))
        {
            return;
        }

        do
        {
            if (entry.th32OwnerProcessID != (uint)pid)
            {
                continue;
            }

            using var hThread = PInvoke.OpenThread_SafeHandle(THREAD_ACCESS_RIGHTS.THREAD_SUSPEND_RESUME, false, entry.th32ThreadID);
            if (hThread.IsInvalid)
            {
                continue;
            }

            action(new HANDLE(hThread.DangerousGetHandle()));
        } while (PInvoke.Thread32Next(snap, ref entry));
    }

    public void HideWindow(int handle)
    {
        var hwnd = new HWND(handle);
        if (handle != 0 && PInvoke.IsWindow(hwnd))
        {
            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_HIDE);
        }
    }

    public void RestoreWindow(int handle)
    {
        var hwnd = new HWND(handle);
        if (handle != 0 && PInvoke.IsWindow(hwnd))
        {
            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
            PInvoke.SetForegroundWindow(hwnd);
        }
    }

    public void CloseWindow(int handle)
    {
        var hwnd = new HWND(handle);
        if (handle != 0 && PInvoke.IsWindow(hwnd))
        {
            PInvoke.PostMessage(hwnd, PInvoke.WM_CLOSE, 0, 0);
        }
    }

    public string GetProcessImagePath(int pid)
    {
        using var hProcess = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (!hProcess.IsInvalid)
        {
            uint size = 1024;
            Span<char> buffer = new char[size];
            if (PInvoke.QueryFullProcessImageName(hProcess, 0, buffer, ref size) && size > 0)
            {
                return buffer[..(int)size].ToString();
            }
        }

        // Fallback: managed API (may throw for protected or already-exited processes)
        try
        {
            using var process = Process.GetProcessById(pid);
            string? path = process.MainModule?.FileName;
            if (string.IsNullOrEmpty(path))
            {
                throw new InvalidOperationException($"Cannot determine executable path for PID {pid}.");
            }
            return path;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Cannot determine executable path for PID {pid}.", ex);
        }
    }

    public unsafe void EnableDebugPrivilege()
    {
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            if (!PInvoke.OpenProcessToken(
                    currentProcess.SafeHandle,
                    TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES | TOKEN_ACCESS_MASK.TOKEN_QUERY,
                    out var hToken))
            {
                return; // best-effort
            }

            using (hToken)
            {
                if (!PInvoke.LookupPrivilegeValue(null, "SeDebugPrivilege", out var luid))
                {
                    return;
                }

                var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1 };
                tp.Privileges[0] = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED };

                var tokenHandle = new HANDLE(hToken.DangerousGetHandle());
                PInvoke.AdjustTokenPrivileges(tokenHandle, false, &tp, 0, null, null);
            }
        }
        catch
        {
            // best-effort; never throw from here
        }
    }

    public void MakeWindowNonActivating(nint handle)
    {
        var hwnd = new HWND(handle);
        int exStyle = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLong(
            hwnd,
            WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            exStyle | (int)(WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW));
    }

    public bool IsWindow(int handle)
    {
        return handle != 0 && PInvoke.IsWindow(new HWND(handle));
    }

    public WindowPlacement MoveWindowOffScreen(int handle)
    {
        var hwnd = new HWND(handle);
        WindowPlacement placement = GetWindowPlacement(handle);

        int virtualScreenLeft = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        int virtualScreenWidth = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
        int virtualScreenRight = virtualScreenLeft + virtualScreenWidth;

        const int Margin = 100;
        int newX = virtualScreenRight + Margin;
        int newY = placement.Bounds.Y;

        // A window still carrying the WS_MAXIMIZE style snaps back to its on-screen maximized rect if
        // merely repositioned via SetWindowPos — the OS re-asserts the maximized position on its own,
        // which looked like "restoring instead of concealing". ShowWindow(SW_RESTORE) clears that style
        // first. The original Maximized state is preserved separately in `placement` (State + NormalBounds)
        // and reapplied atomically by RestoreWindowPosition.
        if (placement.State == WindowState.Maximized)
        {
            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
        }

        PInvoke.SetWindowPos(
            hwnd,
            default(HWND),
            newX,
            newY,
            placement.Bounds.Width,
            placement.Bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

        return placement;
    }

    /// <summary>
    /// Restores a window to <paramref name="placement"/>'s captured state via SetWindowPlacement (not
    /// SetWindowPos): this re-applies the original showCmd (Normal/Maximized/Minimized) together with
    /// rcNormalPosition in one atomic call, so a window that was maximized when thumbnailed comes back
    /// maximized (on the right monitor) instead of landing as an ordinary window sized to the whole screen.
    /// </summary>
    public void RestoreWindowPosition(int handle, WindowPlacement placement)
    {
        var hwnd = new HWND(handle);
        if (handle == 0 || !PInvoke.IsWindow(hwnd))
        {
            return;
        }

        var wp = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!PInvoke.GetWindowPlacement(hwnd, ref wp))
        {
            return;
        }

        wp.showCmd = placement.State switch
        {
            WindowState.Maximized => SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED,
            WindowState.Minimized => SHOW_WINDOW_CMD.SW_SHOWMINIMIZED,
            WindowState.Hidden => SHOW_WINDOW_CMD.SW_HIDE,
            _ => SHOW_WINDOW_CMD.SW_SHOWNORMAL
        };
        wp.rcNormalPosition = placement.NormalBounds;

        PInvoke.SetWindowPlacement(hwnd, wp);
    }

    public void ResizeWindow(int handle, int width, int height)
    {
        var hwnd = new HWND(handle);
        if (handle == 0 || !PInvoke.IsWindow(hwnd))
        {
            return;
        }

        PInvoke.SetWindowPos(
            hwnd,
            default(HWND),
            0,
            0,
            width,
            height,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE);
    }

    public int HideFromTaskbar(int handle)
    {
        var hwnd = new HWND(handle);
        if (handle == 0 || !PInvoke.IsWindow(hwnd))
        {
            return 0;
        }

        int originalExStyle = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        int hiddenExStyle =
            (originalExStyle & ~(int)WINDOW_EX_STYLE.WS_EX_APPWINDOW) | (int)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;
        ApplyExtendedStyleForTaskbar(hwnd, hiddenExStyle);
        return originalExStyle;
    }

    public void RestoreExtendedStyle(int handle, int originalExStyle)
    {
        var hwnd = new HWND(handle);
        if (handle == 0 || !PInvoke.IsWindow(hwnd))
        {
            return;
        }

        ApplyExtendedStyleForTaskbar(hwnd, originalExStyle);
    }

    // Changing WS_EX_TOOLWINDOW/WS_EX_APPWINDOW alone doesn't make Explorer re-evaluate a window's taskbar
    // button — a hide/show cycle is required to force the refresh. Callers only invoke this while the
    // window is positioned off-screen, so the brief hide/show is never visible to the user.
    private static void ApplyExtendedStyleForTaskbar(HWND hwnd, int exStyle)
    {
        PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_HIDE);
        PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle);
        PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
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
