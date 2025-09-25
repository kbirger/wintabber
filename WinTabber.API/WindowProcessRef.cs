using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.API
{
    public class WindowProcessRef(Process process, ApplicationRef application, WindowManager windowManager) : WindowOwner(windowManager)
    {
        public Process Process { get; } = process;

        public ApplicationRef Application { get; } = application;

        public override WindowRef[] GetWindows()
        {
            return WindowManager.Interop.EnumerateProcessWindowHandles(Process.Id)
                .Order()
                .Select(handle => new WindowRef(handle, this))
                .ToArray();
        }

        protected override void AssertOwnsWindow(WindowRef window)
        {
            if(window.Process != this)
                            {
                throw new InvalidOperationException("The specified window is not owned by this process.");
            }
        }
    }
}
