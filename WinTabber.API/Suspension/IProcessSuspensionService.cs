using DynamicData;

namespace WinTabber.API.Suspension;

public interface IProcessSuspensionService : IDisposable
{
    IObservable<IChangeSet<SuspendedWindowEntry, int>> Connect();

    /// <summary>Replayed; starts with the current value, distinct until changed.</summary>
    IObservable<bool> HasSuspendedChanges { get; }

    bool IsSuspended(int pid);

    /// <summary>Testable core — takes plain data, no WindowRef.</summary>
    bool CanSuspend(int pid, bool isProcessElevated);

    /// <summary>Testable core — takes plain data, no WindowRef.</summary>
    bool Suspend(int pid, string processName, string title, IReadOnlyList<int> windowHandles);

    bool CanSuspend(WindowRef window);

    bool Suspend(WindowRef window);

    bool Resume(int pid);

    void ResumeAll();
}
