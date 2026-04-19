using System.Diagnostics;

namespace WinTabber.API;

public interface IProcessRepository
{
    Process[] GetProcesses();
    Process[] GetProcessesByName(string name);
    int GetCurrentProcessId();
    string GetCurrentProcessName();
}
