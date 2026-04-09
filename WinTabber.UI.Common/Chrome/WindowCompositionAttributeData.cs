using System.Runtime.InteropServices;

namespace WinTabber.UI.Common.Chrome;

[StructLayout(LayoutKind.Sequential)]
    public struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }
