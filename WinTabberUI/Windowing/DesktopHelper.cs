using System.Windows;
using System.Windows.Media;
using iNKORE.UI.WPF.DragDrop.Utilities;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
namespace WinTabberUI.Windowing;

internal static class DesktopHelper
{
    /// <summary>
    /// Converts a device-pixel screen rectangle to WPF logical units using the DPI in effect for
    /// <paramref name="visual"/> right now. Queried live rather than cached, so centering is always
    /// correct even if the window's own DPI bookkeeping is stale.
    /// </summary>
    public static Rect ToLogicalBounds(this Visual visual, System.Drawing.Rectangle deviceRect)
    {
        var screenRect = new Rect(deviceRect.Left, deviceRect.Top, deviceRect.Width, deviceRect.Height);
        var dpi = VisualTreeHelper.GetDpi(visual);
        return DpiHelper.DeviceRectToLogical(screenRect, dpi.DpiScaleX, dpi.DpiScaleY);
    }

    public static unsafe Rect GetDesktopArea()
    {
        //PInvoke.SystemParametersInfoForDpi()
        RECT area = new RECT();
        PInvoke.SystemParametersInfo(Windows.Win32.UI.WindowsAndMessaging.SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWORKAREA, 0, &area, 0);

        return new Rect(area.X, area.Y, area.Width, area.Height);
    }

    public static unsafe void SetDesktopArea(Rect rect)
    {
        //PInvoke.SystemParametersInfoForDpi()
        RECT area = new RECT((int)rect.Left, (int)rect.Top, (int)rect.Right, (int)rect.Bottom);
        RECT area2 = new RECT((int)rect.Left, (int)rect.Top, (int)rect.Right, (int)rect.Bottom);
        var ret = PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWORKAREA, 0, &area, 0);
        if(ret > 0)
        {

        }
    }
}
