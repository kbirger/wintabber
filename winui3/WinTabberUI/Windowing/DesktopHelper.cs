using Windows.Foundation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinTabberUI.Windowing;

internal static class DesktopHelper
{
    /// <summary>
    /// The app's single icon file, shared by every window and the tray icon
    /// (<see cref="Coordinators.NotifyIconCoordinator" />'s own <c>ms-appx:///Assets/logo.ico</c>
    /// reference). Unlike that tray icon's <c>BitmapImage</c>, <c>AppWindow.SetIcon</c> takes a real
    /// filesystem path, not an ms-appx URI -- this app is unpackaged, so <c>AppContext.BaseDirectory</c>
    /// (where csproj-default Assets content is copied alongside the exe) is the correct root.
    /// </summary>
    public static readonly string AppIconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");


    /// <summary>
    /// Converts a device-pixel screen rectangle to WinUI 3 logical (effective-pixel) units using
    /// the DPI in effect for the window at <paramref name="hwnd"/> right now. Queried live rather
    /// than cached, so centering is always correct even if the window's own DPI bookkeeping is stale.
    /// </summary>
    public static Rect ToLogicalBounds(nint hwnd, System.Drawing.Rectangle deviceRect)
    {
        var scale = GetScaleForWindow(hwnd);
        return new Rect(
            deviceRect.Left / scale,
            deviceRect.Top / scale,
            deviceRect.Width / scale,
            deviceRect.Height / scale);
    }

    /// <summary>
    /// The DIP-to-physical-pixel scale in effect for <paramref name="hwnd"/> right now (queried
    /// live, not cached). <see cref="Microsoft.UI.Xaml.Window.Bounds"/> and everything measured off
    /// it (e.g. a XAML element's <c>ActualWidth</c>/<c>DesiredSize</c>) are DIPs; AppWindow APIs
    /// (<c>Move</c>, <c>ResizeClient</c>) take physical pixels — this is the conversion factor
    /// between the two, needed anywhere a DIP-space computation feeds an AppWindow call.
    /// </summary>
    public static double GetScaleForWindow(nint hwnd)
    {
        var dpi = PInvoke.GetDpiForWindow(new HWND(hwnd));
        return dpi / 96.0;
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
