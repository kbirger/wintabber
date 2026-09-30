using System.Drawing;
using WinTabber.Api.Media.ShellApplications.Caching;

namespace WinTabber.Api.Media.Tests.ShellApplications.Caching;

public sealed class FakeInstalledApplicationCacheStore(IReadOnlyList<CachedApplicationEntry>? seedEntries = null)
    : IInstalledApplicationCacheStore
{
    private readonly IReadOnlyList<CachedApplicationEntry> _seedEntries = seedEntries ?? [];

    public IReadOnlyList<CachedApplicationEntry> Load() => _seedEntries;

    public byte[]? LoadIconBytes(CachedApplicationEntry entry) =>
        IconBytesByAumid.TryGetValue(entry.AppUserModelId, out var bytes) ? bytes : null;

    public Bitmap? LoadIcon(CachedApplicationEntry entry)
    {
        LoadIconCallCount++;
        return BitmapsByAumid.GetValueOrDefault(entry.AppUserModelId);
    }

    /// <summary>Lets a test pre-populate what a "previously cached" icon's raw bytes were, keyed by AUMID.</summary>
    public Dictionary<string, byte[]?> IconBytesByAumid { get; } = new();

    /// <summary>Lets a test pre-populate the decoded bitmap <see cref="LoadIcon"/> returns, keyed by AUMID.</summary>
    public Dictionary<string, Bitmap?> BitmapsByAumid { get; } = new();

    public bool ThrowOnNextSave { get; set; }
    public int LoadIconCallCount { get; private set; }
    public int SaveCount { get; private set; }
    public IReadOnlyList<CachedApplicationEntry>? SavedEntries { get; private set; }
    public IReadOnlyDictionary<string, byte[]?>? SavedIconBytesByAumid { get; private set; }

    public IReadOnlyList<CachedApplicationEntry> Save(
        IReadOnlyList<CachedApplicationEntry> entries,
        IReadOnlyDictionary<string, byte[]?> iconBytesByAumid
    )
    {
        if (ThrowOnNextSave)
        {
            ThrowOnNextSave = false;
            throw new IOException("Simulated save failure");
        }

        SaveCount++;
        SavedEntries = entries;
        SavedIconBytesByAumid = iconBytesByAumid;

        // Mirrors the real store: the blob is rebuilt from exactly the bytes passed in, so an entry
        // absent from the map loses its icon.
        IconBytesByAumid.Clear();
        var updated = new List<CachedApplicationEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (iconBytesByAumid.TryGetValue(entry.AppUserModelId, out var bytes) && bytes is { Length: > 0 })
            {
                IconBytesByAumid[entry.AppUserModelId] = bytes;
                updated.Add(entry with { IconOffset = 0, IconLength = bytes.Length });
            }
            else
            {
                updated.Add(entry with { IconOffset = -1, IconLength = 0 });
            }
        }
        return updated;
    }
}
