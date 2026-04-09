using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace WinTabberUI.Chrome;

internal static class PeekHelper
{
    public unsafe static void HideFromPeek(nint handle)
    {
        BOOL x = true;
        DWMWINDOWATTRIBUTE a = DWMWINDOWATTRIBUTE.DWMWA_DISALLOW_PEEK;
        var ret = PInvoke.DwmSetWindowAttribute(new HWND(handle), a, &x, sizeof(DWMWINDOWATTRIBUTE));
    }

    public unsafe static void ExcludeFromPeek(nint handle)
    {
        BOOL x = true;
        DWMWINDOWATTRIBUTE a = DWMWINDOWATTRIBUTE.DWMWA_EXCLUDED_FROM_PEEK;
        var ret =PInvoke.DwmSetWindowAttribute(new HWND(handle), a, &x, sizeof(DWMWINDOWATTRIBUTE));
    }
}
