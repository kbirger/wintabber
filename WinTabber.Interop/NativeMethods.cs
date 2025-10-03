using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;

namespace WinTabber.Interop;

public class NativeMethods
{

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
            PInvoke.EnumThreadWindows((uint)thread.Id,
                (hWnd, lParam) =>
                {
                    if(PInvoke.IsWindowVisible(hWnd))
                    {
                        handles.Add(((IntPtr)hWnd).ToInt32());
                    }
                    return true;
                }, IntPtr.Zero);
        }
        return handles;
    }





    

    //[DllImport("dwmapi.dll", EntryPoint = "#113", CallingConvention = CallingConvention.StdCall)]
    //public static extern int DwmpActivateLivePreview([MarshalAs(UnmanagedType.Bool)] bool fActivate, IntPtr hWndExclude, IntPtr hWndInsertBefore, LivePreviewTrigger lpt, IntPtr prcFinalRect);

    //[DllImport("dwmapi.dll")]
    //public static extern int DwmRegisterThumbnail(IntPtr hwndDestination, IntPtr hwndSource, out IntPtr phThumbnail);

    //[DllImport("dwmapi.dll")]
    //public static extern int DwmUnregisterThumbnail(IntPtr thumb);

    //[DllImport("dwmapi.dll")]
    //public static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out PSIZE size);
    //[DllImport("dwmapi.dll")]
    //public static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnail, ref DWM_THUMBNAIL_PROPERTIES pptProperties);

}


/// <summary>
///// Options for DwmpActivateLivePreview
///// </summary>
//public enum LivePreviewTrigger
//{
//    /// <summary>
//    /// Show Desktop button
//    /// </summary>
//    ShowDesktop = 1,

//    /// <summary>
//    /// WIN+SPACE hotkey
//    /// </summary>
//    WinSpace,

//    /// <summary>
//    /// Hover-over Superbar thumbnails
//    /// </summary>
//    Superbar,

//    /// <summary>
//    /// Alt-Tab
//    /// </summary>
//    AltTab,

//    /// <summary>
//    /// Press and hold on Superbar thumbnails
//    /// </summary>
//    SuperbarTouch,

//    /// <summary>
//    /// Press and hold on Show desktop
//    /// </summary>
//    ShowDesktopTouch,
//}