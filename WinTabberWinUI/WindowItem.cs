using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using WinTabber.API;

namespace WinTabberUI
{
    public class WindowItem
    {
        public WindowItem(WindowRef windowRef)
        {
            WindowRef = windowRef ?? throw new ArgumentNullException(nameof(windowRef));
            //Icon = WindowRef.GetIcon().ToImageSource(); 
        }

        public WindowRef WindowRef { get; }

        public string ProcessName => WindowRef.Process.Process.ProcessName;

        public string Title => WindowRef.Title;

        public IntPtr Handle => WindowRef.Handle;
        //public ImageSource Icon { get; set; }

        public void Activate() => WindowRef.BringToFront();
    }
}
