using System.Diagnostics;

namespace WinTabber.API;

[DebuggerDisplay("{ProcessName}", Name = "WindowProcessRef")]
public partial class WindowProcessRef : WindowOwner
{
    public WindowProcessRef(Process process, ApplicationRef application)
    {
        ProcessInstance = process;
        Application = application;
    }
    public Process ProcessInstance { get; }

    public override WindowManager Manager => Application.Manager;

    public ApplicationRef Application { get; }

    [Lazy]
    private bool GetIsProcessElevated()
    {
        return Manager.Interop.IsProcessElevated(ProcessInstance);
    }
    internal WindowRef NewWindow(int handle)
    {
        return new WindowRef(handle, this);
    }

    public bool IsValid => ProcessInstance.Id > 1
        && ProcessInstance.Id != Application.Manager.ProcessRepository.GetCurrentProcessId()
        && Application.IsValidProcess;

    public override WindowRef[] GetWindows()
    {
        var fgWindow = Manager.Interop.GetForegroundWindowHandle();
        return Manager.Interop.EnumerateProcessWindowHandles(ProcessInstance)
            .OrderBy(handle => handle != fgWindow)
            .ThenBy(handle => handle)
            .Select(NewWindow)
            //.Where(window => window.Title != string.Empty) // Filter out windows without titles (often invisible or non-interactive windows)
            .Where(window => window.IsValidUserWindow)
            .ToArray();
    }

    protected override void AssertOwnsWindow(WindowRef window)
    {
        if(window.Process != this)
                        {
            throw new InvalidOperationException("The specified window is not owned by this _process.");
        }
    }
}
