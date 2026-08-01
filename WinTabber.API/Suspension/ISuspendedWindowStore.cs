namespace WinTabber.API.Suspension;

/// <summary>Abstracts persistence so the service is unit-testable.</summary>
public interface ISuspendedWindowStore
{
    /// <summary>Returns the persisted entries, or empty if the backing store is missing or corrupt. Never throws.</summary>
    IReadOnlyList<SuspendedWindowEntry> Load();

    void Save(IEnumerable<SuspendedWindowEntry> entries);

    void Delete();
}
