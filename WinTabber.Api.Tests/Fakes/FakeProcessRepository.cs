using System.Diagnostics;
using WinTabber.API;

namespace WinTabber.Api.Tests.Fakes;

public sealed class FakeProcessRepository : IProcessRepository
{
    public int CurrentProcessId { get; set; } = 1234;
    public string CurrentProcessName { get; set; } = "WinTabberUI";

    public Process[] GetProcesses() => [];

    public Process[] GetProcessesByName(string name) => [];

    public int GetCurrentProcessId() => CurrentProcessId;

    public string GetCurrentProcessName() => CurrentProcessName;
}
