using System.Diagnostics;

namespace WinTabber.API;

public sealed class ProcessRepository : IProcessRepository
{
    private static readonly int _currentPid = Process.GetCurrentProcess().Id;
    private static readonly string _currentName = Process.GetCurrentProcess().ProcessName;

    public Process[] GetProcesses() => Process.GetProcesses();
    public Process[] GetProcessesByName(string name) => Process.GetProcessesByName(name);
    public int GetCurrentProcessId() => _currentPid;
    public string GetCurrentProcessName() => _currentName;
}
