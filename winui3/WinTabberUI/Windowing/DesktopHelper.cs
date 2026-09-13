using Windows.Foundation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinTabberUI.Windowing;

internal static class DesktopHelper
{
    /// <summary>
    /// Converts a device-pixel screen rectangle to WinUI 3 logical (effective-pixel) units using
    /// the DPI in effect for the window at <paramref name="hwnd"/> right now. Queried live rather
    /// than cached, so centering is always correct even if the window's own DPI bookkeeping is stale.
    /// </summary>
    public static Rect ToLogicalBounds(nint hwnd, System.Drawing.Rectangle deviceRect)
    {
        var dpi = PInvoke.GetDpiForWindow(new HWND(hwnd));
        var scale = dpi / 96.0;
        return new Rect(
            deviceRect.Left / scale,
            deviceRect.Top / scale,
            deviceRect.Width / scale,
            deviceRect.Height / scale);
    }

    public static unsafe Rect GetDesktopArea()
    {
        RECT area = new RECT();
        PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWORKAREA, 0, &area, 0);
        return new Rect(area.X, area.Y, area.Width, area.Height);
    }

    public static unsafe void SetDesktopArea(Rect rect)
    {
        RECT area = new RECT((int)rect.Left, (int)rect.Top, (int)(rect.Left + rect.Width), (int)(rect.Top + rect.Height));
        PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWORKAREA, 0, &area, 0);
    }
}
