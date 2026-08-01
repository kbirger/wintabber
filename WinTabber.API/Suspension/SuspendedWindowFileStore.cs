using System.Text.Json;

namespace WinTabber.API.Suspension;

/// <summary>
/// Reads and writes suspended_state.json. No business logic — I/O only.
/// </summary>
public sealed class SuspendedWindowFileStore : ISuspendedWindowStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _directory;
    private readonly string _path;

    public SuspendedWindowFileStore(string directory)
    {
        _directory = directory;
        _path = Path.Combine(directory, "suspended_state.json");
    }

    public IReadOnlyList<SuspendedWindowEntry> Load()
    {
        if (!File.Exists(_path))
            return [];

        try
        {
            string json = File.ReadAllText(_path);
            var entries = JsonSerializer.Deserialize<List<SuspendedWindowEntry>>(json, JsonOptions);
            return entries ?? [];
        }
        catch
        {
            // Corrupt file: start fresh rather than crashing.
            return [];
        }
    }

    public void Save(IEnumerable<SuspendedWindowEntry> entries)
    {
        // First run has no directory yet; without this the service's ctor-time prune Save throws
        // and a Suspend would leave the process frozen but unrecorded.
        Directory.CreateDirectory(_directory);

        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(entries.ToList(), JsonOptions));
        File.Move(tmp, _path, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
