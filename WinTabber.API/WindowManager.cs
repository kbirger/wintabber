using WinTabber.API.CircularBuffer;
using WinTabber.Interop;

namespace WinTabber.API;

public class WindowManager : WindowOwner
{
    public WindowManager(IInteropProxy interop, IProcessRepository processRepository)
    {
        Interop = interop;
        ProcessRepository = processRepository;
    }

    internal IInteropProxy Interop { get; }
    internal IProcessRepository ProcessRepository { get; }

    internal WindowTitleStore TitleStore { get; } = new WindowTitleStore();

    public override WindowManager Manager => this;

    internal ApplicationRef NewApplicationRef(string processName)
    {
        return new ApplicationRef(processName, this);
    }


    public override WindowRef[] GetWindows()
    {
        return ProcessRepository.GetProcesses()
            // .Where(process => !string.IsNullOrWhiteSpace(process.MainWindowTitle))
            .GroupBy(process => process.ProcessName)
            .SelectMany(processGroup => NewApplicationRef(processGroup.Key).GetWindows(processGroup))
            .OrderBy(w => w.Handle)
            .ToArray();
    }

    public ApplicationRef[] GetApplications()
    {
        return ProcessRepository.GetProcesses()
            .GroupBy(Process => Process.ProcessName)
            .Select(processGroup => NewApplicationRef(processGroup.Key))
            .OrderBy(a => a.ProcessName)
            .ToArray();
    }

    public ApplicationRef? GetCurrentApplication()
    {
        if (Interop.GetForegroundProcess() is { } process)
        {
            return NewApplicationRef(process.ProcessName);
        }

        return null;
    }

    public WindowRef? GetWindow(int handle)
    {
        var process = Interop.GetWindowProcess(handle);
        if (process is null)
        {
            return null;
        }
        return NewApplicationRef(process.ProcessName)
            .NewWindowProcessRef(process)
            .NewWindow(handle);
    }

    public ApplicationRef GetApplication(string processName)
    {
        return NewApplicationRef(processName);
    }

    public WindowProcessRef? GetCurrentProcess()
    {
        if (Interop.GetForegroundProcess() is { } process)
        {
            return NewApplicationRef(process.ProcessName).NewWindowProcessRef(process);
        }
        return null;
    }

    protected override void AssertOwnsWindow(WindowRef window)
    {
        if (window.Process.Manager != this)
        {
            throw new InvalidOperationException("The specified window is not owned by this window manager.");
        }
    }

    private CircularBuffer<int> _windowActivationHistory = new CircularBuffer<int>(100);

    public void RegisterForegroundWindowChanged(int handle)
    {
        var window = GetWindow(handle);
        if(window is not null)
        {
            _windowActivationHistory.PushFront(handle);
        }
    }

    internal Dictionary<int, int> GetWindowOrder(IEnumerable<int> handles)
    {
        var query = new HashSet<int>(handles);
        List<KeyValuePair<int, int>> pairs = new();
        int i = 0;
        foreach (var handle in _windowActivationHistory)
        {
            if (query.Contains(handle))
            {
                pairs.Add(new(handle, i++));
                query.Remove(handle);
            }
        }

        return new Dictionary<int, int>(pairs);
    }

    public void EndPreview()
    {
        Interop.DeactivateLivePreview();
    }
}
