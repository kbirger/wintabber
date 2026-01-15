using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinTabberUI.Chrome;
using WinTabberUI.Views;

namespace WinTabberUI.Helpers;

public static class WindowRasterizationHelper
{
    public static void AnimateHide(Window window)
    {

        var imageSource = RasterizeWindow(window);

        var fader = new FadingWindow
        {
            DataContext = imageSource,
            SizeToContent = SizeToContent.Manual
        };

        fader.Top = window.Top;
        fader.Left = window.Left;
        fader.Width = window.Width;
        fader.Height = window.Height;

        fader.Show();
        fader.Topmost = true;
        window.Hide();
        fader.FadeOut();
    }

    private static ImageSource RasterizeWindow(Window window)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        RenderTargetBitmap target = new RenderTargetBitmap(
            (int)window.RenderSize.Width,
            (int)window.RenderSize.Height,
            96,
            96,
            PixelFormats.Default);
        target.Render(window);

        return target;
    }

    public static void AnimateShow(Window window)
    {
        var hwndSource = (HwndSource)PresentationSource.FromVisual(window);
        var top = window.Top;
        var left = window.Left;

        window.Top = -99999;
        window.Left = -99999;

        window.Show();

        //CloakHelper.Cloak(hwndSource.Handle);
        var imageSource = RasterizeWindow(window);
        window.Hide();
        window.Top = top;
        window.Left = left;
        //CloakHelper.Uncloak(hwndSource.Handle);

        var fader = new FadingWindow
        {
            DataContext = imageSource,
            SizeToContent = SizeToContent.Manual
        };
        fader.Top = window.Top;
        fader.Left = window.Left;
        fader.Width = window.Width;
        fader.Height = window.Height;
        fader.Topmost = true;

        fader.FadeIn(window);
    }
}
