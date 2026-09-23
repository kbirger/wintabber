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

    public Bitmap? LoadIcon(CachedApplicationEntry entry) => null;

    /// <summary>Lets a test pre-populate what a "previously cached" icon's raw bytes were, keyed by AUMID.</summary>
    public Dictionary<string, byte[]?> IconBytesByAumid { get; } = new();

    public IReadOnlyList<CachedApplicationEntry>? SavedEntries { get; private set; }
    public IReadOnlyDictionary<string, byte[]?>? SavedIconBytesByAumid { get; private set; }

    public void Save(IReadOnlyList<CachedApplicationEntry> entries, IReadOnlyDictionary<string, byte[]?> iconBytesByAumid)
    {
        SavedEntries = entries;
        SavedIconBytesByAumid = iconBytesByAumid;
    }
}
