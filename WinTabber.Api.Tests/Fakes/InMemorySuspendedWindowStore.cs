using WinTabber.API.Suspension;

namespace WinTabber.Api.Tests.Fakes;

public sealed class InMemorySuspendedWindowStore : ISuspendedWindowStore
{
    private List<SuspendedWindowEntry> _entries = [];

    public bool Deleted { get; private set; }
    public int SaveCount { get; private set; }

    /// <summary>Seeds the store with entries, as if loaded from a previous run.</summary>
    public void Seed(params SuspendedWindowEntry[] entries) => _entries = [.. entries];

    public IReadOnlyList<SuspendedWindowEntry> Load() => _entries;

    public void Save(IEnumerable<SuspendedWindowEntry> entries)
    {
        _entries = entries.ToList();
        SaveCount++;
        Deleted = false;
    }

    public void Delete()
    {
        _entries = [];
        Deleted = true;
    }
}
