using System.Diagnostics;

namespace WinTabber.API
{
    public class ApplicationRef(string processName, WindowManager windowManager) : WindowOwner(windowManager)
    {
        public string ProcessName { get; } = processName;
        public override WindowRef[] GetWindows()
        {
            return Process.GetProcessesByName(ProcessName)
                .SelectMany(p => new WindowProcessRef(p, this, WindowManager).GetWindows())
                .OrderBy(w => w.Handle)
                .ToArray();                
        }

        protected override void AssertOwnsWindow(WindowRef window)
        {
            if(window.Process.Application != this)
            {
                throw new InvalidOperationException("The specified window is not owned by this application.");
            }
        }
    }
}