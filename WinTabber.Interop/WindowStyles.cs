using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinTabber.Interop
{
    public class WindowStyles
    {
        internal WindowStyles()
        {

        }
        internal static WindowStyles FromFlags(WINDOW_EX_STYLE styles)
        {
            return new WindowStyles
            {
                AppWindow = styles.HasFlag(WINDOW_EX_STYLE.WS_EX_APPWINDOW),
                ToolWindow = styles.HasFlag(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW),
                CannotBeActivated = styles.HasFlag(WINDOW_EX_STYLE.WS_EX_NOACTIVATE),
                IsTopMost = styles.HasFlag(WINDOW_EX_STYLE.WS_EX_TOPMOST),
            };
        }
        public bool IsTopMost { get; init; }
        public bool CannotBeActivated { get; init; }
        public bool ToolWindow { get; init; }
        public bool AppWindow { get; init; }
    }
}