using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
namespace WinTabberUI.Windowing
{
    internal static class DesktopHelper
    {
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
            PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWORKAREA, 0, &area, 0);
        }
    }
}
