namespace WinTabber.API.Suspension;

/// <summary>
/// Immutable snapshot of a single suspended process entry.
/// </summary>
public sealed record SuspendedWindowEntry(
    int ProcessId,
    IReadOnlyList<int> WindowHandles,
    string PathHash,
    string ProcessName,
    string Title,
    string StrategyName = "process"
);
