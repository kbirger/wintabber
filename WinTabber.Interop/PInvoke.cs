using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Windows.Win32
{
    internal static partial class PInvoke
    {
        /// <summary>
        /// Options for DwmpActivateLivePreview
        /// </summary>
        public enum LivePreviewTrigger
        {
            /// <summary>
            /// Show Desktop button
            /// </summary>
            ShowDesktop = 1,

            /// <summary>
            /// WIN+SPACE hotkey
            /// </summary>
            WinSpace,

            /// <summary>
            /// Hover-over Superbar thumbnails
            /// </summary>
            Superbar,

            /// <summary>
            /// Alt-Tab
            /// </summary>
            AltTab,

            /// <summary>
            /// Press and hold on Superbar thumbnails
            /// </summary>
            SuperbarTouch,

            /// <summary>
            /// Press and hold on Show desktop
            /// </summary>
            ShowDesktopTouch
        };

        [DllImport("dwmapi.dll", EntryPoint = "#113", CallingConvention = CallingConvention.StdCall)]
        public static extern int DwmpActivateLivePreview([MarshalAs(UnmanagedType.Bool)] bool fActivate, IntPtr hWndExclude, IntPtr hWndInsertBefore, LivePreviewTrigger lpt, IntPtr prcFinalRect);
    }
}
