using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTabber.API;

namespace WinTabberUI
{
    public class WindowItem
    {
        public WindowItem(WindowRef windowRef)
        {
            WindowRef = windowRef ?? throw new ArgumentNullException(nameof(windowRef));
        }

        public WindowRef WindowRef { get; }

        public string ProcessName => WindowRef.Process.Process.ProcessName;

        public string Title => WindowRef.Title;

        public void Activate() => WindowRef.BringToFront();
    }
}
