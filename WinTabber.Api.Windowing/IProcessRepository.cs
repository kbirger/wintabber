using System.Diagnostics;

namespace WinTabber.Api.Windowing;

public interface IProcessRepository
{
    Process[] GetProcesses();
    Process[] GetProcessesByName(string name);
    int GetCurrentProcessId();
    string GetCurrentProcessName();
}
