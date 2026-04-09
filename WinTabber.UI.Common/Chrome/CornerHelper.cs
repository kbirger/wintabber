using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace WinTabber.UI.Common.Chrome;

public static class CornerHelper
{
    internal unsafe static void SetCornerPreference(nint handle, DWM_WINDOW_CORNER_PREFERENCE value)
    {
        DWM_WINDOW_CORNER_PREFERENCE x = value;
        DWMWINDOWATTRIBUTE a = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
        Windows.Win32.PInvoke.DwmSetWindowAttribute(new HWND(handle), a, &x, sizeof(DWMWINDOWATTRIBUTE));
    }
}
