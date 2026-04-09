using System.Runtime.InteropServices;

namespace WinTabber.UI.Common.Chrome;

[StructLayout(LayoutKind.Sequential)]
    public struct AccentPolicy
    {
        public AccentState AccentState;
        public AccentFlags AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }
