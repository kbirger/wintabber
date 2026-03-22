using Microsoft.VisualBasic.Devices;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinTabberUI;

internal class WindowHelper2
{
    public static readonly DependencyProperty WindowAlphaProperty = DependencyProperty.RegisterAttached(
        nameof(WindowAlphaProperty),
        typeof(byte),
        typeof(Window),
        new PropertyMetadata((byte)255, OnAlphaChanged)
    );
    private static readonly int WS_EX_LAYERED = 0x80000;

    private static void OnAlphaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window)
        {
            return;
        }

        if (e.NewValue is not byte alpha)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
        {
            return;
        }
        ApplyOpacity(window, alpha);
        //var currentAlpha = GetAlphaInternal(hwnd);
        //var h = new HWND(hwnd);
        //SetLayered(h);
        //SetAlphaInternal(alpha, hwnd);
    }

    private static void SetAlphaInternal(byte alpha, nint hwnd)
    {
        var h = new HWND(hwnd);
        var x = PInvoke.SetLayeredWindowAttributes;

        PInvoke.SetLayeredWindowAttributes(h, new COLORREF(0), 254, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
    }

    private static unsafe byte GetAlphaInternal(nint hwnd)
    {
        byte alpha = 0;
        COLORREF color = new COLORREF();
        var flag = Windows.Win32.UI.WindowsAndMessaging.LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA;
        var result = PInvoke.GetLayeredWindowAttributes(new HWND(hwnd), &color, &alpha, &flag);

        return alpha;
    }

    public static void Reset(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var h = new HWND(hwnd);
        //var currentAlpha = GetAlphaInternal(hwnd);
        window.InvalidateVisual();

        //PInvoke.SetLayeredWindowAttributes(h, new COLORREF(0), 255, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
        //SetNoLayered(h);
        Redraw(h);
        //SetAlphaInternal(255, hwnd);
    }

    public static void SetLayered(HWND hwnd)
    {
        var extendedStyle = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var ret = PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, extendedStyle | WS_EX_LAYERED);
        Redraw(hwnd);

    }

    public static void Redraw(HWND hwnd)
    {
        var ret = PInvoke.RedrawWindow(
            hwnd,
            null,
            null,
            REDRAW_WINDOW_FLAGS.RDW_ERASENOW 
                | REDRAW_WINDOW_FLAGS.RDW_ERASE
                | REDRAW_WINDOW_FLAGS.RDW_INVALIDATE
                | REDRAW_WINDOW_FLAGS.RDW_FRAME
                | REDRAW_WINDOW_FLAGS.RDW_ALLCHILDREN
        );
    }

    public unsafe static void SetNoLayered(HWND hwnd)
    {
        var extendedStyle = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var ret = PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, extendedStyle & ~WS_EX_LAYERED);
        BLENDFUNCTION b = new BLENDFUNCTION();
        //var ret2 = PInvoke.UpdateLayeredWindow(hwnd, HDC.Null, null, null, HDC.Null, null, new COLORREF(0xff), b, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
        Redraw(hwnd);
    }

   

    public static void SetWindowAlpha(Window window, byte opacity)
    {
        window.SetValue(WindowAlphaProperty, opacity);
    }


    public static void ApplyOpacity(Window window, byte opacity)
    {
        var hwnd = new WindowInteropHelper(window).Handle;

        var dpiInfo = VisualTreeHelper.GetDpi(window);

        int width = (int)(window.ActualWidth * dpiInfo.DpiScaleX);
        int height = (int)(window.ActualHeight * dpiInfo.DpiScaleY);

        if (width == 0 || height == 0) { return; }
        var rtb = new RenderTargetBitmap(
            width,
            height,
            dpiInfo.PixelsPerInchX, dpiInfo.PixelsPerInchY,
            PixelFormats.Pbgra32);

        rtb.Render(window);

        IntPtr hBitmap = CreateHBitmap(rtb);
        IntPtr screenDC = GetDC(IntPtr.Zero);
        IntPtr memDC = CreateCompatibleDC(screenDC);
        IntPtr oldBitmap = SelectObject(memDC, hBitmap);

        var blend = new BLENDFUNCTION
        {
            BlendOp = AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = opacity,
            AlphaFormat = AC_SRC_ALPHA
        };

        SIZE size = new SIZE(width, height);
        POINT ptSrc = new POINT() { x = 0, y = 0 };

        UpdateLayeredWindow(
            hwnd,
            screenDC,
            IntPtr.Zero,
            ref size,
            memDC,
            ref ptSrc,
            0,
            ref blend,
            ULW_ALPHA);

        // Cleanup
        SelectObject(memDC, oldBitmap);
        DeleteObject(hBitmap);
        DeleteDC(memDC);
        ReleaseDC(IntPtr.Zero, screenDC);
    }

    static IntPtr CreateHBitmap(BitmapSource source)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        int stride = width * 4;

        byte[] pixels = new byte[height * stride];
        source.CopyPixels(pixels, stride, 0);

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB
            }
        };

        IntPtr bits;
        IntPtr hBitmap = CreateDIBSection(
            IntPtr.Zero,
            ref bmi,
            DIB_RGB_COLORS,
            out bits,
            IntPtr.Zero,
            0);

        Marshal.Copy(pixels, 0, bits, pixels.Length);
        return hBitmap;
    }

    #region Win32

    const int ULW_ALPHA = 0x00000002;
    const byte AC_SRC_OVER = 0x00;
    const byte AC_SRC_ALPHA = 0x01;
    const int BI_RGB = 0;
    const int DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }

    [StructLayout(LayoutKind.Sequential)]
    struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("user32.dll")]
    static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        IntPtr pptDst,
        ref SIZE psize,
        IntPtr hdcSrc,
        ref POINT pptSrc,
        int crKey,
        ref BLENDFUNCTION pblend,
        int dwFlags);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BITMAPINFO pbmi,
        int iUsage,
        out IntPtr ppvBits,
        IntPtr hSection,
        int dwOffset);

    #endregion
}
