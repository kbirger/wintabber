using System.Diagnostics;

namespace WinTabber.API;

[DebuggerDisplay("{ProcessName}", Name = "ApplicationRef")]

public class ApplicationRef(string processName, WindowManager windowManager) : WindowOwner
{
    public string ProcessName { get; } = processName;
    public override WindowRef[] GetWindows()
    {
        return GetWindows(Process.GetProcessesByName(ProcessName));
    }
    internal WindowRef[] GetWindows(IEnumerable<Process> processes)
    {
        var fgWindow = Manager.Interop.GetForegroundWindowHandle();
        return processes
            .Where(ValidateProcesses)
            .SelectMany(process => NewWindowProcessRef(process).GetWindows())
            .Where(ValidateWindow) // Filter out windows without titles (often invisible or non-interactive windows)
            .OrderBy(w => w.Handle == fgWindow)
            .ThenBy(w => w.Handle)
            .ToArray();
    }

    public override WindowManager Manager { get; } = windowManager;

    private bool ValidateProcesses(Process process)
    {
        return process.Id > 0 && !string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
    }

    public WindowRef[] GetWindows2()
    {
        //var fgWindow = WindowManager.Interop.GetForegroundWindowHandle();
        var processes = Process.GetProcessesByName(ProcessName)
            .Where(ValidateProcesses)
            .SelectMany(process => NewWindowProcessRef(process).GetWindows())
            .Where(ValidateWindow) // Filter out windows without titles (often invisible or non-interactive windows)
                                   //.OrderBy(w => w.Handle == fgWindow)
                                   //.ThenBy(w => w.Handle)
            .ToArray();

        var lookup = Manager.GetWindowOrder(processes.Select(processes => processes.Handle));

        var x = processes
            .OrderBy(w => lookup.TryGetValue(w.Handle, out var idx) ? idx : int.MaxValue)
            .ThenBy(w => w.Handle) // Then by handle to ensure consistent ordering
            .ToArray();
        //var zz = string.Join('|', x.Select(z => z.Title + " " + z.Handle));
        return x;
    }
    private static bool ValidateWindow(WindowRef window)
    {
        //return window.Title != string.Empty;
        return window.IsValidUserWindow;
    }

    protected override void AssertOwnsWindow(WindowRef window)
    {
        if (window.Process.Application != this)
        {
            throw new InvalidOperationException("The specified window is not owned by this application.");
        }
    }

    internal WindowProcessRef NewWindowProcessRef(Process process)
    {
        return new WindowProcessRef(process, this);
    }
}