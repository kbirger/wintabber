using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTabber.Interop;

namespace WinTabber.API
{
    public class WindowManager : WindowOwner
    {
        public WindowManager(IInteropProxy interop)
            :base()
        {
            WindowManager = this;
            Interop = interop;
        }

        internal IInteropProxy Interop { get; }

        public override WindowRef[] GetWindows()
        {
            return Process.GetProcesses()
                .GroupBy(Process => Process.ProcessName)
                .SelectMany(processGroup => new ApplicationRef(processGroup.Key, this).GetWindows())
                .OrderBy(w => w.Handle)
                .ToArray();
        }

        public ApplicationRef[] GetApplications()
        {
            return Process.GetProcesses()
                .GroupBy(Process => Process.ProcessName)
                .Select(processGroup => new ApplicationRef(processGroup.Key, this))
                .OrderBy(a => a.ProcessName)
                .ToArray();
        }

        public ApplicationRef? GetCurrentApplication()
        {
            return new ApplicationRef(Interop.GetForegroundProcess().ProcessName, this);            
        }

        public WindowProcessRef? GetCurrentProcess()
        {
            var proc = Interop.GetForegroundProcess();
            return new WindowProcessRef(proc, new ApplicationRef(proc.ProcessName, this), this);
        }

        protected override void AssertOwnsWindow(WindowRef window)
        {
            if(window.Process.WindowManager != this)
            {
                throw new InvalidOperationException("The specified window is not owned by this window manager.");
            }
        }
    }
}
