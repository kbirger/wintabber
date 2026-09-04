using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace WinTabber.Interop;

public class NativeMethods
{
    private static Lazy<HashSet<nint>> _lazyInvalidHwnds;
    static NativeMethods()
    {
        _lazyInvalidHwnds = new Lazy<HashSet<nint>>(() =>
        {
            return new HashSet<nint>([
                nint.Zero,
                PInvoke.GetDesktopWindow(),
                PInvoke.GetShellWindow()
            ]);
        });
    }

    private static HashSet<nint> InvalidHwnds => _lazyInvalidHwnds.Value;

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
                    if(ShouldIncludeWindow(hWnd))
                    {
                        handles.Add(((IntPtr)hWnd).ToInt32());
                    }
                    return true;
                }, IntPtr.Zero);
        }
        return handles;
    }


    private static bool ShouldIncludeWindow(HWND hwnd)
    {
        return !InvalidHwnds.Contains(hwnd)
            && PInvoke.IsWindowVisible(hwnd);
    }
}
