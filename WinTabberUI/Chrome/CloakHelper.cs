using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Foundation;
using Windows.Win32;

namespace WinTabberUI.Chrome;

internal static class CloakHelper
{
    public unsafe static void Cloak(nint handle)
    {
        BOOL x = true;
        DWMWINDOWATTRIBUTE a = DWMWINDOWATTRIBUTE.DWMWA_CLOAKED;
        var ret = PInvoke.DwmSetWindowAttribute(new HWND(handle), a, &x, sizeof(DWMWINDOWATTRIBUTE));
        a = DWMWINDOWATTRIBUTE.DWMWA_CLOAK;
        ret = PInvoke.DwmSetWindowAttribute(new HWND(handle), a, &x, sizeof(DWMWINDOWATTRIBUTE));

    }

    public unsafe static void Uncloak(nint handle)
    {
        BOOL x = false;
        DWMWINDOWATTRIBUTE a = DWMWINDOWATTRIBUTE.DWMWA_CLOAKED;
        var ret =PInvoke.DwmSetWindowAttribute(new HWND(handle), a, &x, sizeof(DWMWINDOWATTRIBUTE));
        a = DWMWINDOWATTRIBUTE.DWMWA_CLOAK;
        ret = PInvoke.DwmSetWindowAttribute(new HWND(handle), a, &x, sizeof(DWMWINDOWATTRIBUTE));

    }
}
