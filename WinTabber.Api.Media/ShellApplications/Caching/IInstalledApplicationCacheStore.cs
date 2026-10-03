using System.Drawing;

namespace WinTabber.Api.Media.ShellApplications.Caching;

/// <summary>
/// The seam over the on-disk installed-application cache. <see cref="Repositories.InstalledApplicationRepository"/>
/// reads through this on construction for a fast first paint, and writes through it once a live
/// Shell scan completes.
/// </summary>
public interface IInstalledApplicationCacheStore
{
    /// <returns>The cached entries, or an empty list if no cache exists yet or it could not be read.</returns>
    IReadOnlyList<CachedApplicationEntry> Load();

    /// <returns>
    /// The raw PNG bytes previously saved for <paramref name="entry"/>, or <see langword="null"/> if
    /// it has none or they could not be read. Used to carry an unchanged app's icon forward into a
    /// new <see cref="Save"/> call without decoding it, and without touching Shell.
    /// </returns>
    byte[]? LoadIconBytes(CachedApplicationEntry entry);

    /// <returns>The cached icon for <paramref name="entry"/> decoded to a <see cref="Bitmap"/>, or <see langword="null"/> if it has none or it could not be read.</returns>
    Bitmap? LoadIcon(CachedApplicationEntry entry);

    /// <summary>
    /// Overwrites the cache with <paramref name="entries"/>. <paramref name="iconBytesByAumid"/> maps
    /// an entry's <see cref="CachedApplicationEntry.AppUserModelId"/> to its PNG-encoded icon bytes;
    /// an AUMID absent from the map, or mapped to <see langword="null"/>, is written with no icon.
    /// Only <see cref="InstalledApplicationCacheWriter"/> calls this, from its single consumer task.
    /// </summary>
    /// <returns>
    /// <paramref name="entries"/> with each entry's icon offset and length updated to where its icon
    /// now sits in the icon blob, so the caller can read those icons back without a reload.
    /// </returns>
    IReadOnlyList<CachedApplicationEntry> Save(
        IReadOnlyList<CachedApplicationEntry> entries,
        IReadOnlyDictionary<string, byte[]?> iconBytesByAumid
    );
}
