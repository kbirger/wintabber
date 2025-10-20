using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.Interop
{
    public class WindowPlacement
    {
        public Rectangle Bounds { get; init; }
        public WindowState State { get; init; }
        public enum WindowState
        {
            Normal,
            Minimized,
            Maximized,
            Hidden
        }
    }
}
