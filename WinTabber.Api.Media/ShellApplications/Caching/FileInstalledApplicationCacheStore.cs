using System.Drawing;
using System.Text.Json;

namespace WinTabber.Api.Media.ShellApplications.Caching;

public sealed class FileInstalledApplicationCacheStore : IInstalledApplicationCacheStore
{
    private readonly string _cacheDirectory;
    private readonly string _metadataFilePath;
    private readonly string _iconBlobFilePath;

    public FileInstalledApplicationCacheStore(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
        _metadataFilePath = Path.Combine(cacheDirectory, "installed-apps.json");
        _iconBlobFilePath = Path.Combine(cacheDirectory, "installed-apps.icons");
    }

    public IReadOnlyList<CachedApplicationEntry> Load()
    {
        try
        {
            using var fileStream = OpenReadTolerantOfConcurrentReplace(_metadataFilePath);
            return JsonSerializer.Deserialize<IReadOnlyList<CachedApplicationEntry>>(fileStream) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public byte[]? LoadIconBytes(CachedApplicationEntry entry)
    {
        if (entry.IconLength <= 0)
        {
            return null;
        }

        try
        {
            using var fileStream = OpenReadTolerantOfConcurrentReplace(_iconBlobFilePath);
            if (entry.IconOffset < 0 || entry.IconOffset + entry.IconLength > fileStream.Length)
            {
                return null;
            }

            fileStream.Seek(entry.IconOffset, SeekOrigin.Begin);
            var buffer = new byte[entry.IconLength];
            fileStream.ReadExactly(buffer);
            return buffer;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public Bitmap? LoadIcon(CachedApplicationEntry entry)
    {
        var bytes = LoadIconBytes(entry);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            using var memoryStream = new MemoryStream(bytes);
            // A Bitmap constructed from a Stream keeps that stream open for its own lifetime, so it
            // must not wrap `memoryStream` directly -- `memoryStream` goes out of scope when this
            // method returns. Constructing a second Bitmap from the first copies the pixel data,
            // detaching it, the same technique InstalledApplicationRepository.CreateBitmapPreservingAlpha
            // already uses for the same reason.
            using var streamBackedBitmap = new Bitmap(memoryStream);
            return new Bitmap(streamBackedBitmap);
        }
        catch (Exception)
        {
            // Thrown by the Bitmap constructor when the bytes are not a valid image (a corrupt or
            // truncated blob file), or by System.Drawing.Common for other decode issues (e.g.
            // OutOfMemoryException on corrupt data) -- treat exactly like a missing icon.
            return null;
        }
    }

    public IReadOnlyList<CachedApplicationEntry> Save(
        IReadOnlyList<CachedApplicationEntry> entries,
        IReadOnlyDictionary<string, byte[]?> iconBytesByAumid
    )
    {
        Directory.CreateDirectory(_cacheDirectory);

        var iconBlobTempPath = _iconBlobFilePath + ".tmp";
        var metadataTempPath = _metadataFilePath + ".tmp";

        var updatedEntries = new List<CachedApplicationEntry>(entries.Count);
        using (var iconBlobStream = File.Open(iconBlobTempPath, FileMode.Create))
        {
            foreach (var entry in entries)
            {
                if (iconBytesByAumid.TryGetValue(entry.AppUserModelId, out var bytes) && bytes is { Length: > 0 })
                {
                    var offset = iconBlobStream.Position;
                    iconBlobStream.Write(bytes);
                    updatedEntries.Add(entry with { IconOffset = offset, IconLength = bytes.Length });
                }
                else
                {
                    updatedEntries.Add(entry with { IconOffset = -1, IconLength = 0 });
                }
            }
        }

        using (var metadataStream = File.Open(metadataTempPath, FileMode.Create))
        {
            JsonSerializer.Serialize(metadataStream, updatedEntries, new JsonSerializerOptions { WriteIndented = true });
        }

        // Move the blob into place before the metadata that references it, so a crash between the
        // two moves leaves, at worst, fresh metadata pointing at a not-yet-updated blob (LoadIcon's
        // bounds/format checks degrade that to "no icon" rather than corrupt data) -- never metadata
        // for icons that no longer exist at all.
        File.Move(iconBlobTempPath, _iconBlobFilePath, overwrite: true);
        File.Move(metadataTempPath, _metadataFilePath, overwrite: true);
        return updatedEntries;
    }

    /// <summary>
    /// A cache-seeded app's icon can be read lazily at any time, including while a fresh scan's
    /// result is being written back via <see cref="Save"/>'s temp-file-then-move. `FileShare.Delete`
    /// is required (beyond the default `FileShare.Read`) because on Windows, <see cref="File.Move(string, string, bool)"/>
    /// with `overwrite: true` onto a path that is open for reading fails with a sharing violation
    /// unless that reader explicitly allowed the file to be replaced.
    /// </summary>
    private static FileStream OpenReadTolerantOfConcurrentReplace(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
