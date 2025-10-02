using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.API
{
    public class WindowProcessRef(Process process, ApplicationRef application) : WindowOwner(application.WindowManager)
    {
        public Process Process { get; } = process;

        public ApplicationRef Application { get; } = application;

        internal WindowRef NewWindow(int handle)
        {
            return new WindowRef(handle, this);
        }

        public override WindowRef[] GetWindows()
        {
            var fgWindow = WindowManager.Interop.GetForegroundWindowHandle();
            return WindowManager.Interop.EnumerateProcessWindowHandles(Process)
                .OrderBy(handle => handle != fgWindow)
                .ThenBy(handle => handle)
                .Select(NewWindow)
                .Where(window => window.Title != string.Empty) // Filter out windows without titles (often invisible or non-interactive windows)
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
