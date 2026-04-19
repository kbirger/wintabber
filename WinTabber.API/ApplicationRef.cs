using System.Diagnostics;
using System.Reactive.Linq;

namespace WinTabber.API;

[DebuggerDisplay("{ProcessName}", Name = "ApplicationRef")]

public partial class ApplicationRef : WindowOwner
{
    public ApplicationRef(string processName, WindowManager windowManager)
        : base()
    {
        ProcessName = processName;
        Manager = windowManager;
        var ignored = GetIgnoredProcesses(windowManager.ProcessRepository);
        IsValidProcess = !ignored.Contains(processName);
    }

    private static HashSet<string> GetIgnoredProcesses(IProcessRepository repo) =>
        new(["idle", repo.GetCurrentProcessName()], StringComparer.OrdinalIgnoreCase);

    public string ProcessName { get; }
    public override WindowRef[] GetWindows()
    {
        return GetWindows(Manager.ProcessRepository.GetProcessesByName(ProcessName));
    }


    internal WindowRef[] GetWindows(IEnumerable<Process> processes)
    {
        var fgWindow = Manager.Interop.GetForegroundWindowHandle();
        var validWindows = processes
            .Where(ValidateProcess)
            .SelectMany(process => NewWindowProcessRef(process).GetWindows())
            .Where(ValidateWindow) // Filter out windows without titles (often invisible or non-interactive windows)
            .ToArray();

        var lookup = Manager.GetWindowOrder(validWindows.Select(process => process.Handle));

        return validWindows
            .OrderBy(w => lookup.TryGetValue(w.Handle, out var idx) ? idx : int.MaxValue)
            .ThenBy(w => w.Handle) // Then by handle to ensure consistent ordering
            .ToArray();
    }

    // public WindowRef[] GetWindows2()
    // {
    //     //var fgWindow = WindowManager.Interop.GetForegroundWindowHandle();
    //     var processes = ProcessMonitor.Processes.Take(1).Wait()[ProcessName]
    //         .Where(ValidateProcess)
    //         .SelectMany(process => NewWindowProcessRef(process).GetWindows())
    //         .Where(ValidateWindow) // Filter out windows without titles (often invisible or non-interactive windows)
    //                                //.OrderBy(w => w.Handle == fgWindow)
    //                                //.ThenBy(w => w.Handle)
    //         .ToArray();

    //     var lookup = Manager.GetWindowOrder(processes.Select(processes => processes.Handle));

    //     var x = processes
    //         .OrderBy(w => lookup.TryGetValue(w.Handle, out var idx) ? idx : int.MaxValue)
    //         .ThenBy(w => w.Handle) // Then by handle to ensure consistent ordering
    //         .ToArray();
    //     //var zz = string.Join('|', x.Select(z => z.Title + " " + z.Handle));
    //     return x;
    // }

    public override WindowManager Manager { get; }

    private bool ValidateProcess(Process process)
    {
        return process.Id > 0 && process.Id != Manager.ProcessRepository.GetCurrentProcessId()
            && process.ProcessName != Manager.ProcessRepository.GetCurrentProcessName();
    }

    public bool IsValidProcess { get; private init; }

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