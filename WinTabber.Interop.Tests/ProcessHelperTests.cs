using WinTabber.Interop;

namespace WinTabber.Interop.Tests;

public class ProcessHelperTests
{
    [Test]
    public async Task IsSystemProcess_ReturnsFalse_ForTheCurrentTestProcess()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();

        bool result = ProcessHelper.IsSystemProcess(current);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsSystemProcess_ByPid_ReturnsTrue_ForPidZero()
    {
        bool result = ProcessHelper.IsSystemProcess(0);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ClassifyNonSystemProcesses_ExcludesPidZero()
    {
        var processes = new[] { new ProcessInfo(0, "System Idle Process", 0) };

        var result = ProcessHelper.ClassifyNonSystemProcesses(processes).ToList();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task ClassifyNonSystemProcesses_ExcludesProcessesNamedSvchost_CaseInsensitive()
    {
        var processes = new[] { new ProcessInfo(100, "SvcHost", 4) };

        var result = ProcessHelper.ClassifyNonSystemProcesses(processes).ToList();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task ClassifyNonSystemProcesses_ExcludesChildOfSystemProcess()
    {
        var processes = new[]
        {
            new ProcessInfo(100, "svchost", 4), // system: named svchost
            new ProcessInfo(200, "child.exe", 100), // system: parent (100) is system
        };

        var result = ProcessHelper.ClassifyNonSystemProcesses(processes).ToList();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task ClassifyNonSystemProcesses_IncludesOrdinaryProcess()
    {
        var processes = new[] { new ProcessInfo(1234, "notepad", 999) };

        var result = ProcessHelper.ClassifyNonSystemProcesses(processes).ToList();

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Id).IsEqualTo(1234);
    }

    [Test]
    public async Task ClassifyNonSystemProcesses_IncludesOrdinaryChildOfOrdinaryParent()
    {
        var processes = new[]
        {
            new ProcessInfo(1000, "explorer.exe", 4),
            new ProcessInfo(1001, "notepad.exe", 1000),
        };

        var result = ProcessHelper.ClassifyNonSystemProcesses(processes).ToList();

        await Assert.That(result.Count).IsEqualTo(2);
    }
}
