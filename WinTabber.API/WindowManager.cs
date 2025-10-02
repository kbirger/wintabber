using System.Diagnostics;
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

        internal ApplicationRef NewApplicationRef(string processName)
        {
            return new ApplicationRef(processName, this);
        }


        public override WindowRef[] GetWindows()
        {
            return Process.GetProcesses()
                .GroupBy(Process => Process.ProcessName)
                .SelectMany(processGroup => NewApplicationRef(processGroup.Key).GetWindows())
                .OrderBy(w => w.Handle)
                .ToArray();
        }

        public ApplicationRef[] GetApplications()
        {
            return Process.GetProcesses()
                .GroupBy(Process => Process.ProcessName)
                .Select(processGroup => NewApplicationRef(processGroup.Key))
                .OrderBy(a => a.ProcessName)
                .ToArray();
        }

        public ApplicationRef? GetCurrentApplication()
        {
            return NewApplicationRef(Interop.GetForegroundProcess().ProcessName);
        }

        public WindowProcessRef? GetCurrentProcess()
        {
            var process = Interop.GetForegroundProcess();
            return NewApplicationRef(process.ProcessName).NewWindowProcessRef(process);
        }

        protected override void AssertOwnsWindow(WindowRef window)
        {
            if(window.Process.WindowManager != this)
            {
                throw new InvalidOperationException("The specified window is not owned by this window manager.");
            }
        }

        public void EndPreview()
        {
            Interop.DeactivateLivePreview();
        }
    }
}
