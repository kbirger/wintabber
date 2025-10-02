using System.Diagnostics;

namespace WinTabber.API
{
    public class ApplicationRef(string processName, WindowManager windowManager) : WindowOwner(windowManager)
    {
        public string ProcessName { get; } = processName;
        public override WindowRef[] GetWindows()
        {
            var fgWindow = WindowManager.Interop.GetForegroundWindowHandle();
            return Process.GetProcessesByName(ProcessName)
                .SelectMany(process => NewWindowProcessRef(process).GetWindows())
                .Where(ValidateWindow) // Filter out windows without titles (often invisible or non-interactive windows)
                .OrderBy(w => w.Handle == fgWindow)
                .ThenBy(w => w.Handle)
                .ToArray();
        }

        private static bool ValidateWindow(WindowRef window)
        {
            return window.Title != string.Empty;
        }

        protected override void AssertOwnsWindow(WindowRef window)
        {
            if(window.Process.Application != this)
            {
                throw new InvalidOperationException("The specified window is not owned by this application.");
            }
        }

        internal WindowProcessRef NewWindowProcessRef(Process process)
        {
            return new WindowProcessRef(process, this);
        }
    }
}