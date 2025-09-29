using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinTabber.Interop;


namespace WinTabberUI
{
    public static class IconExtensions
    {
        public static ImageSource ToImageSource(this Icon icon)
        {
            if (icon is null) return null;
            Bitmap bitmap = icon.ToBitmap();
            IntPtr hBitmap = bitmap.GetHbitmap();

            try
            {
                ImageSource wpfBitmap = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                return wpfBitmap;
            }
            finally
            {
                if (!NativeMethods.DeleteObject(hBitmap))
                {
                    throw new Win32Exception();
                }
            }
        }
    }
}
