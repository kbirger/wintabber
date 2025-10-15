using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;

namespace WinTabberUI.Chrome;

internal class Interop
{
    [DllImport("user32.dll")]
    public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);


    public static void EnableBlur(nint handle, AccentState accentState, uint color)
    {
        
    }

    internal static void SetAccentPolicy(IntPtr hWnd, AccentState accentState, AccentFlags accentFlags, uint gradientColor)
    {

        var accent = new AccentPolicy
        {
            AccentState = accentState,
            AccentFlags = accentFlags,
            AnimationId = 0,
            GradientColor = gradientColor
        };

        var accentStructSize = Marshal.SizeOf(accent);
        var accentPtr = Marshal.AllocHGlobal(accentStructSize);
        Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            SizeOfData = accentStructSize,
            Data = accentPtr
        };


        SetWindowCompositionAttribute(hWnd, ref data);

        Marshal.FreeHGlobal(accentPtr);
    }


}
