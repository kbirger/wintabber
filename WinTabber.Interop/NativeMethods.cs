using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinTabber.Interop;

public class NativeMethods
{
    /// <summary>
    ///     Brings the thread that created the specified window into the foreground and activates the window. Keyboard input is
    ///     directed to the window, and various visual cues are changed for the user. The system assigns a slightly higher
    ///     priority to the thread that created the foreground window than it does to other threads.
    ///     <para>See for https://msdn.microsoft.com/en-us/library/windows/desktop/ms633539%28v=vs.85%29.aspx more information.</para>
    /// </summary>
    /// <param name="hWnd">
    ///     C++ ( hWnd [in]. Type: HWND )<br />A handle to the window that should be activated and brought to the foreground.
    /// </param>
    /// <returns>
    ///     <c>true</c> or nonzero if the window was brought to the foreground, <c>false</c> or zero If the window was not
    ///     brought to the foreground.
    /// </returns>
    /// <remarks>
    ///     The system restricts which processes can set the foreground window. A process can set the foreground window only if
    ///     one of the following conditions is true:
    ///     <list type="bullet">
    ///     <listheader>
    ///         <term>Conditions</term><description></description>
    ///     </listheader>
    ///     <item>The process is the foreground process.</item>
    ///     <item>The process was started by the foreground process.</item>
    ///     <item>The process received the last input event.</item>
    ///     <item>There is no foreground process.</item>
    ///     <item>The process is being debugged.</item>
    ///     <item>The foreground process is not a Modern Application or the Start Screen.</item>
    ///     <item>The foreground is not locked (see LockSetForegroundWindow).</item>
    ///     <item>The foreground lock time-out has expired (see SPI_GETFOREGROUNDLOCKTIMEOUT in SystemParametersInfo).</item>
    ///     <item>No menus are active.</item>
    ///     </list>
    ///     <para>
    ///     An application cannot force a window to the foreground while the user is working with another window.
    ///     Instead, Windows flashes the taskbar button of the window to notify the user.
    ///     </para>
    ///     <para>
    ///     A process that can set the foreground window can enable another process to set the foreground window by
    ///     calling the AllowSetForegroundWindow function. The process specified by dwProcessId loses the ability to set
    ///     the foreground window the next time the user generates input, unless the input is directed at that process, or
    ///     the next time a process calls AllowSetForegroundWindow, unless that process is specified.
    ///     </para>
    ///     <para>
    ///     The foreground process can disable calls to SetForegroundWindow by calling the LockSetForegroundWindow
    ///     function.
    ///     </para>
    /// </remarks>
    // For Windows Mobile, replace user32.dll with coredll.dll
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, ShowWindowCommands nCmdShow);


    [DllImport("user32.dll")]
    public static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    public static extern int SendMessage(IntPtr hWnd, int msg, SystemCommand wParam);

    public const int WM_SYSCOMMAND = 0x0112;
    public const int WM_GETICON = 0x007F;
    public const int ICON_SMALL = 0;
    public const int ICON_LARGE = 1;
    public const int ICON_SMALL2 = 2;
    public enum ShowWindowCommands : int
    {
        Hide = 0,
        Normal = 1,
        ShowMinimized = 2,
        Maximize = 3, // is this the right value?
        ShowMaximized = 3,
        ShowNoActivate = 4,
        Show = 5,
        Minimize = 6,
        ShowMinNoActive = 7,
        ShowNA = 8,
        Restore = 9,
        ShowDefault = 10,
        ForceMinimize = 11
    }

    // enum for WM_SYSCOMMAND etc...
    public enum SystemCommand : int
    {
        Size = 0xF000,
        Move = 0xF010,
        Minimize = 0xF020,
        Maximize = 0xF030,
        NextWindow = 0xF040,
        PreviousWindow = 0xF050,
        Close = 0xF060,
        VScroll = 0xF070,
        HScroll = 0xF080,
        MouseMenu = 0xF090,
        KeyMenu = 0xF100,
        ArrangeWindows = 0xF110,
        Restore = 0xF120,
        TaskList = 0xF130,
        ScreenSave = 0xF140,
        HotKey = 0xF150,
        Default = 0xF160,
        MonitorPower = 0xF170,
        ContextHelp = 0xF180,
        Separator = 0xF00F
    }

    // definition for EnumerateProcessWindowHandles
    public static IEnumerable<int> EnumerateProcessWindowHandles(int processId)
    {
        return EnumerateProcessWindowHandles(Process.GetProcessById(processId));
    }
    public static IEnumerable<int> EnumerateProcessWindowHandles(Process process)
    {
        var handles = new List<int>();
        foreach (ProcessThread thread in process.Threads)
        {
            EnumThreadWindows(thread.Id,
                (hWnd, lParam) =>
                {
                    if(IsWindowVisible(hWnd))
                    {
                        handles.Add(hWnd.ToInt32());
                    }
                    return true;
                }, IntPtr.Zero);
        }
        return handles;
    }

    [DllImport("kernel32")]
    public static extern IntPtr GetConsoleWindow();


    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);
    public delegate bool EnumThreadDelegate(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool EnumThreadWindows(int dwThreadId, EnumThreadDelegate lpfn, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    public struct WINDOWPLACEMENT
                {
        public int length;
        public int flags;
        public ShowWindowCommands showCmd;
        public System.Drawing.Point ptMinPosition;
        public System.Drawing.Point ptMaxPosition;
        public System.Drawing.Rectangle rcNormalPosition;
    }

    public static WINDOWPLACEMENT GetWindowPlacement(IntPtr hWnd)
    {
        var placement = new WINDOWPLACEMENT();
        placement.length = Marshal.SizeOf(placement);
        GetWindowPlacement(hWnd, ref placement);

        return placement;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);


    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr LoadIcon(IntPtr hInstance, int lpIconName);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("dwmapi.dll", EntryPoint = "#113", CallingConvention = CallingConvention.StdCall)]
    public static extern int DwmpActivateLivePreview([MarshalAs(UnmanagedType.Bool)] bool fActivate, IntPtr hWndExclude, IntPtr hWndInsertBefore, LivePreviewTrigger lpt, IntPtr prcFinalRect);

    [DllImport("dwmapi.dll")]
    public static extern int DwmRegisterThumbnail(IntPtr hwndDestination, IntPtr hwndSource, out IntPtr phThumbnail);

    [DllImport("dwmapi.dll")]
    public static extern int DwmUnregisterThumbnail(IntPtr thumb);

    //[DllImport("dwmapi.dll")]
    //public static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out PSIZE size);
    //[DllImport("dwmapi.dll")]
    //public static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnail, ref DWM_THUMBNAIL_PROPERTIES pptProperties);

}


/// <summary>
/// Options for DwmpActivateLivePreview
/// </summary>
public enum LivePreviewTrigger
{
    /// <summary>
    /// Show Desktop button
    /// </summary>
    ShowDesktop = 1,

    /// <summary>
    /// WIN+SPACE hotkey
    /// </summary>
    WinSpace,

    /// <summary>
    /// Hover-over Superbar thumbnails
    /// </summary>
    Superbar,

    /// <summary>
    /// Alt-Tab
    /// </summary>
    AltTab,

    /// <summary>
    /// Press and hold on Superbar thumbnails
    /// </summary>
    SuperbarTouch,

    /// <summary>
    /// Press and hold on Show desktop
    /// </summary>
    ShowDesktopTouch,
}