# Installed-Application File Cache Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `InstalledApplicationRepository` load from a disk cache on startup, so the app picker shows applications immediately, then merge the live Shell scan result on top of that cache and overwrite the disk cache once the scan finishes.

**Architecture:** `InstalledApplicationRepository` keeps its own `SourceCache<InstalledApplicationInfo, string>` (`_apps`, currently declared but unused in the class — this plan puts it to work instead of deleting it). On construction it seeds `_apps` synchronously from a small on-disk JSON file, so the app picker has content immediately. The live Shell scan still runs the same way it does today, in the background; when it produces a result, the repository explicitly computes which cached keys are no longer present and removes them, then adds or overwrites every fresh entry — a manual `SourceCache.Edit` merge, not `ToObservableChangeSet` diffing (see "Design decisions verified empirically" below for why the built-in diffing was rejected). Once merged, the repository writes the fresh result back to disk in the background: metadata to a JSON file, icon bytes (PNG-encoded) appended to a single companion blob file, with each metadata entry recording its icon's byte offset and length. An app whose metadata hasn't changed since the last write reuses its already-encoded icon bytes rather than re-extracting the icon from Shell. `AppCache` (dead code, superseded by this work) is deleted.

**Tech Stack:** .NET 10, DynamicData 9.4.1, System.Reactive, System.Text.Json, System.Drawing (PNG encoding), TUnit.

**Spec:** No separate spec document — this plan was scoped directly from the user's request in conversation, and refined against two points raised in an implementation-review pass (see "Design decisions verified empirically"). The Global Constraints below capture the decisions made there.

## Design decisions verified empirically

Two behavioral assumptions about DynamicData 9.4.1 were checked against the actual installed package (`~/.nuget/packages/dynamicdata/9.4.1`) with small throwaway console programs before this plan was finalized, rather than assumed from memory of the API:

1. **`ToObservableChangeSet(this IObservable<IEnumerable<T>>, keySelector, expireAfter, ...)` has no equality-comparer parameter and never suppresses an update for a "key-equal" item.** A concern was raised that `InstalledApplicationInfo.Equals` (which compares only `AppUserModelId`) might make this operator treat a cached item and its fresh counterpart as unchanged, silently keeping the stale `Icon`/`TargetPath`. Verified false: emitting `[item]` then `[itemWithSameKeyDifferentName]` through this operator produces `Add` then `Update`, and the cache ends up holding the fresh object. **Not a real risk** — no code below needs to work around it.
2. **The same operator is additive, not diff-and-replace.** Emitting `[a, b]` then `[b2, c]` produces `Add a`, `Add b`, `Update b→b2`, `Add c` — `a` is never removed. This **is** a real problem for this feature: an uninstalled app would keep showing in the app picker until its `expireAfter` (1 day) elapses, not immediately. This is why the plan below does **not** use `ToObservableChangeSet` over a concatenated cached+live sequence — it merges manually through `SourceCache.Edit`, which was also verified empirically to remove and update correctly in one step.

## Global Constraints

- Icon storage is a JSON metadata file plus one combined binary blob file for icon bytes (user's explicit choice) — not a file-per-icon and not a single embedded database file.
- `AppCache` (`WinTabber.Infrastructure\AppCache.cs`) is dead code: it is registered in DI, `Load()` is called once at startup, but `Load()`'s body is fully commented out, and no other code calls `GetByAumid`/`GetByPath`. Delete it entirely as part of this plan, do not merge it into the new design.
- `WinTabber.Api.Media` must not gain a project reference to `WinTabber.Infrastructure` (that dependency already runs the other way — `WinTabber.Infrastructure` references `WinTabber.Api.Media`). The new cache-store implementation takes a plain directory path string via its constructor; the app-level `Paths` class supplies that path from the Bootstrapper, in `WinTabberUI`.
- A cache read/parse/decode failure of any kind must never throw out of the repository — fall back to treating the cache as empty, matching the defensive style already used by `ApplicationSettings.Load()`.
- A failed live Shell acquisition must not wipe out an already-seeded cache. Only a *successful* scan result is merged in (see Task 2).
- An uninstalled app must disappear from `ApplicationsByAumid`/`ApplicationsByPath` as soon as the next successful scan completes, not linger until a 1-day expiry (see "Design decisions verified empirically" above). The pre-existing `expireAfter: TimeSpan.FromDays(1)` this replaces is not carried forward: `GetInstalledApplicationsObservable()` runs exactly once per process (`Refresh()` is dead code — nothing subscribes to `_refreshSubject`), so a background expiry timer in a long-running tray app would silently drop an installed app from the picker after 24 hours with no scan left to bring it back. `MergeFreshApps` is the correct, explicit replacement for what expiry was a safety net for.
- Persisting the cache after a scan must not force every app's icon to be re-extracted from Shell on every write — only apps that are new, or whose `Name`/`TargetPath`/`PackageInstallPath` changed since the last write, get their icon re-encoded; everything else carries its already-encoded icon bytes forward unchanged. Otherwise this feature would convert today's lazy, on-demand icon load into an eager full-catalog decode on every single launch, which works against its own goal of a faster launch.
- Writing the cache to disk must not corrupt or transiently break a concurrently-open read of the previous version of the same files (a seeded app's lazily-loaded icon observable can subscribe and read `installed-apps.icons` at any time, including while a scan result is being written back). Writes go to temp files, moved into place afterward; reads use a `FileShare` mode that tolerates a concurrent move.

---

## File Structure

**New files:**
- `WinTabber.Api.Media\ShellApplications\Caching\CachedApplicationEntry.cs` — one row of cached metadata (AUMID, name, paths, icon blob offset/length).
- `WinTabber.Api.Media\ShellApplications\Caching\IInstalledApplicationCacheStore.cs` — the seam `InstalledApplicationRepository` depends on.
- `WinTabber.Api.Media\ShellApplications\Caching\FileInstalledApplicationCacheStore.cs` — the concrete JSON + blob-file implementation.
- `WinTabber.Api.Media.Tests\ShellApplications\Caching\FileInstalledApplicationCacheStoreTests.cs`
- `WinTabber.Api.Media.Tests\ShellApplications\Caching\FakeInstalledApplicationCacheStore.cs` (test fake, colocated with the caching tests since only they use it)
- `WinTabber.Api.Media.Tests\ShellApplications\InstalledApplicationRepositoryPersistenceTests.cs` — tests for the internal merge and cache-entry-building logic added to the repository.

**Modified files:**
- `WinTabber.Api.Media\ShellApplications\Repositories\InstalledApplicationRepository.cs` — take `IInstalledApplicationCacheStore` as a second constructor parameter, seed `_apps` from it, merge live scans into `_apps` explicitly, persist to it in the background.
- `WinTabber.Api.Media\WinTabber.Api.Media.csproj` — add `InternalsVisibleTo` for the test project.
- `WinTabber.Api.Media.Tests\ShellApplications\InstalledApplicationRepositoryTests.cs` — update the existing test's constructor call, add the "cache survives a failed scan" test.
- `WinTabber.Api.Media.Tests\README.md` — document the new coverage.
- `WinTabberUI\Bootstrapper.cs` and `winui3\WinTabberUI\Bootstrapper.cs` — register `IInstalledApplicationCacheStore`, remove `AppCache` registration.
- `WinTabberUI\BackgroundServiceContainer.cs` and `winui3\WinTabberUI\BackgroundServiceContainer.cs` — remove the `AppCache` startup call.

**Deleted files:**
- `WinTabber.Infrastructure\AppCache.cs`

---

### Task 1: Cache-store seam and its file-backed implementation

**Files:**
- Create: `WinTabber.Api.Media\ShellApplications\Caching\CachedApplicationEntry.cs`
- Create: `WinTabber.Api.Media\ShellApplications\Caching\IInstalledApplicationCacheStore.cs`
- Create: `WinTabber.Api.Media\ShellApplications\Caching\FileInstalledApplicationCacheStore.cs`
- Test: `WinTabber.Api.Media.Tests\ShellApplications\Caching\FileInstalledApplicationCacheStoreTests.cs`

**Interfaces:**
- Produces: `CachedApplicationEntry` (record, `AppUserModelId`, `Name`, `TargetPath`, `PackageInstallPath`, `IconOffset` (`long`, `-1` = no icon), `IconLength` (`int`, `0` = no icon)).
- Produces: `IInstalledApplicationCacheStore` with `IReadOnlyList<CachedApplicationEntry> Load()`, `byte[]? LoadIconBytes(CachedApplicationEntry entry)`, `System.Drawing.Bitmap? LoadIcon(CachedApplicationEntry entry)`, `void Save(IReadOnlyList<CachedApplicationEntry> entries, IReadOnlyDictionary<string, byte[]?> iconBytesByAumid)`.
- Produces: `FileInstalledApplicationCacheStore(string cacheDirectory)` implementing the interface above — Task 2 consumes this via DI, and its carry-forward logic consumes `LoadIconBytes` specifically.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using WinTabber.Api.Media.ShellApplications.Caching;

namespace WinTabber.Api.Media.Tests.ShellApplications.Caching;

public class FileInstalledApplicationCacheStoreTests
{
    private static string CreateTempCacheDirectory() =>
        Path.Combine(Path.GetTempPath(), "WinTabberCacheTests_" + Guid.NewGuid().ToString("N"));

    [Test]
    public async Task Load_ReturnsEmpty_WhenNoCacheFilesExist()
    {
        var directory = CreateTempCacheDirectory();
        try
        {
            var store = new FileInstalledApplicationCacheStore(directory);

            var entries = store.Load();

            await Assert.That(entries).IsEmpty();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task Load_ReturnsEmpty_WhenMetadataFileIsCorrupt()
    {
        var directory = CreateTempCacheDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "installed-apps.json"), "{ not valid json");
            var store = new FileInstalledApplicationCacheStore(directory);

            var entries = store.Load();

            await Assert.That(entries).IsEmpty();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task Save_ThenLoad_RoundTripsMetadata()
    {
        var directory = CreateTempCacheDirectory();
        try
        {
            var store = new FileInstalledApplicationCacheStore(directory);
            var entries = new[]
            {
                new CachedApplicationEntry
                {
                    AppUserModelId = "App.One",
                    Name = "App One",
                    TargetPath = @"C:\Apps\One.exe",
                    PackageInstallPath = null,
                },
                new CachedApplicationEntry
                {
                    AppUserModelId = "App.Two",
                    Name = "App Two",
                    TargetPath = null,
                    PackageInstallPath = @"C:\Program Files\Two",
                },
            };

            store.Save(entries, new Dictionary<string, byte[]?>());
            var loaded = store.Load();

            await Assert.That(loaded.Count).IsEqualTo(2);
            var one = loaded.Single(e => e.AppUserModelId == "App.One");
            await Assert.That(one.Name).IsEqualTo("App One");
            await Assert.That(one.TargetPath).IsEqualTo(@"C:\Apps\One.exe");
            var two = loaded.Single(e => e.AppUserModelId == "App.Two");
            await Assert.That(two.PackageInstallPath).IsEqualTo(@"C:\Program Files\Two");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task Save_ThenLoadIconBytes_RoundTripsRawBytes()
    {
        var directory = CreateTempCacheDirectory();
        try
        {
            var store = new FileInstalledApplicationCacheStore(directory);
            byte[] iconBytes = [1, 2, 3, 4, 5];
            var entry = new CachedApplicationEntry { AppUserModelId = "App.Icon", Name = "App Icon" };

            store.Save([entry], new Dictionary<string, byte[]?> { ["App.Icon"] = iconBytes });
            var loadedEntry = store.Load().Single();
            var loadedBytes = store.LoadIconBytes(loadedEntry);

            await Assert.That(loadedBytes).IsEquivalentTo(iconBytes);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task Save_ThenLoadIcon_DecodesToBitmap()
    {
        var directory = CreateTempCacheDirectory();
        try
        {
            var store = new FileInstalledApplicationCacheStore(directory);
            using var sourceBitmap = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
            sourceBitmap.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));
            using var pngStream = new MemoryStream();
            sourceBitmap.Save(pngStream, ImageFormat.Png);
            var iconBytes = pngStream.ToArray();

            var entry = new CachedApplicationEntry { AppUserModelId = "App.Icon", Name = "App Icon" };
            store.Save([entry], new Dictionary<string, byte[]?> { ["App.Icon"] = iconBytes });
            var loadedEntry = store.Load().Single();

            using var loadedIcon = store.LoadIcon(loadedEntry);

            await Assert.That(loadedIcon).IsNotNull();
            await Assert.That(loadedIcon!.Width).IsEqualTo(4);
            await Assert.That(loadedIcon.GetPixel(0, 0)).IsEqualTo(Color.FromArgb(255, 10, 20, 30));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task LoadIcon_And_LoadIconBytes_ReturnNull_WhenEntryHasNoCachedIcon()
    {
        var directory = CreateTempCacheDirectory();
        try
        {
            var store = new FileInstalledApplicationCacheStore(directory);
            var entry = new CachedApplicationEntry { AppUserModelId = "App.NoIcon", Name = "No Icon" };

            await Assert.That(store.LoadIcon(entry)).IsNull();
            await Assert.That(store.LoadIconBytes(entry)).IsNull();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task Save_LeavesNoTempFilesBehind()
    {
        var directory = CreateTempCacheDirectory();
        try
        {
            var store = new FileInstalledApplicationCacheStore(directory);
            var entry = new CachedApplicationEntry { AppUserModelId = "App.One", Name = "App One" };

            store.Save([entry], new Dictionary<string, byte[]?>());

            var remainingTempFiles = Directory.GetFiles(directory, "*.tmp");
            await Assert.That(remainingTempFiles).IsEmpty();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test WinTabber.Api.Media.Tests -- --treenode-filter "/*/*/FileInstalledApplicationCacheStoreTests/*"`
Expected: FAIL to compile — `FileInstalledApplicationCacheStore` and `CachedApplicationEntry` do not exist yet.

- [ ] **Step 3: Write `CachedApplicationEntry`**

```csharp
namespace WinTabber.Api.Media.ShellApplications.Caching;

public sealed record CachedApplicationEntry
{
    public required string AppUserModelId { get; init; }
    public required string Name { get; init; }
    public string? TargetPath { get; init; }
    public string? PackageInstallPath { get; init; }

    /// <summary>Byte offset of this app's icon in the icon blob file. -1 when no icon is cached.</summary>
    public long IconOffset { get; init; } = -1;

    /// <summary>Length in bytes of this app's icon in the icon blob file. 0 when no icon is cached.</summary>
    public int IconLength { get; init; }
}
```

- [ ] **Step 4: Write `IInstalledApplicationCacheStore`**

```csharp
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
    /// </summary>
    void Save(IReadOnlyList<CachedApplicationEntry> entries, IReadOnlyDictionary<string, byte[]?> iconBytesByAumid);
}
```

- [ ] **Step 5: Write `FileInstalledApplicationCacheStore`**

```csharp
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
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
        catch (JsonException)
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
        catch (ArgumentException)
        {
            // Thrown by the Bitmap constructor when the bytes are not a valid image (a corrupt or
            // truncated blob file) -- treat exactly like a missing icon.
            return null;
        }
    }

    public void Save(IReadOnlyList<CachedApplicationEntry> entries, IReadOnlyDictionary<string, byte[]?> iconBytesByAumid)
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
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test WinTabber.Api.Media.Tests -- --treenode-filter "/*/*/FileInstalledApplicationCacheStoreTests/*"`
Expected: PASS (7 tests)

- [ ] **Step 7: Commit**

```bash
git add WinTabber.Api.Media/ShellApplications/Caching WinTabber.Api.Media.Tests/ShellApplications/Caching
git commit -m "feat: add file-backed installed-application cache store"
```

---

### Task 2: Wire the cache store into `InstalledApplicationRepository`

**Files:**
- Modify: `WinTabber.Api.Media\ShellApplications\Repositories\InstalledApplicationRepository.cs`
- Modify: `WinTabber.Api.Media\WinTabber.Api.Media.csproj`
- Modify: `WinTabber.Api.Media.Tests\ShellApplications\InstalledApplicationRepositoryTests.cs`
- Test: `WinTabber.Api.Media.Tests\ShellApplications\Caching\FakeInstalledApplicationCacheStore.cs`
- Test: `WinTabber.Api.Media.Tests\ShellApplications\InstalledApplicationRepositoryPersistenceTests.cs`

**Interfaces:**
- Consumes: `IInstalledApplicationCacheStore` from Task 1 (`Load()`, `LoadIconBytes(CachedApplicationEntry)`, `LoadIcon(CachedApplicationEntry)`, `Save(...)`).
- Consumes: `CachedApplicationEntry` from Task 1.
- Produces: `InstalledApplicationRepository(IShellApplicationSource, IInstalledApplicationCacheStore)` — Task 3's DI registration consumes this new constructor shape.
- Produces (internal, for tests only): `InstalledApplicationRepository.MergeFreshApps(IReadOnlyList<InstalledApplicationInfo> freshApps)` and `InstalledApplicationRepository.TryPersistCache(IReadOnlyList<InstalledApplicationInfo> freshApps)` — both instance methods, both safe to call directly and synchronously in a test without going through the constructor's background scan or its `Task.Run` wrapper.

**Step 1 below is a fake needed by both this task's tests and the updated existing test — write it first.**

- [ ] **Step 1: Write the test fake**

```csharp
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
```

- [ ] **Step 2: Write the failing tests**

First, the merge and persistence-logic unit tests. These call `MergeFreshApps`/`TryPersistCache` directly, bypassing the constructor's own background scan entirely (the fake shell source below always throws, so the constructor's own scan never produces a result to interfere) — this sidesteps the project's existing, documented limitation that `ShellObject` has no accessible test constructor, since `InstalledApplicationInfo` itself is a plain object these tests can construct freely:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.Reactive.Linq;
using WinTabber.Api.Media.ShellApplications.Caching;
using WinTabber.Api.Media.ShellApplications.Models;
using WinTabber.Api.Media.ShellApplications.Repositories;
using WinTabber.Api.Media.Tests.Fakes;

namespace WinTabber.Api.Media.Tests.ShellApplications;

public class InstalledApplicationRepositoryPersistenceTests
{
    private static InstalledApplicationRepository CreateRepository(
        IReadOnlyList<CachedApplicationEntry>? seedEntries = null,
        out FakeInstalledApplicationCacheStore cacheStore
    )
    {
        cacheStore = new FakeInstalledApplicationCacheStore(seedEntries);
        var source = new FakeShellApplicationSource(() =>
            throw new InvalidOperationException("Not exercised by these tests")
        );
        return new InstalledApplicationRepository(source, cacheStore);
    }

    [Test]
    public async Task MergeFreshApps_UpdatesAnExistingKey_EvenThoughEqualityOnlyComparesAumid()
    {
        using var repository = CreateRepository(
            [new CachedApplicationEntry { AppUserModelId = "App.One", Name = "Old Name" }],
            out _
        );

        repository.MergeFreshApps([
            new InstalledApplicationInfo
            {
                AppUserModelId = "App.One",
                Name = "New Name",
                TargetPath = @"C:\New\Path.exe",
                Icon = Observable.Return((Bitmap?)null),
            },
        ]);

        var updated = repository.ApplicationsByAumid.Lookup("App.One");
        await Assert.That(updated.HasValue).IsTrue();
        await Assert.That(updated.Value.Name).IsEqualTo("New Name");
        await Assert.That(updated.Value.TargetPath).IsEqualTo(@"C:\New\Path.exe");
    }

    [Test]
    public async Task MergeFreshApps_RemovesAKey_AbsentFromTheFreshList()
    {
        using var repository = CreateRepository(
            [new CachedApplicationEntry { AppUserModelId = "App.Uninstalled", Name = "Gone" }],
            out _
        );

        repository.MergeFreshApps([]);

        await Assert.That(repository.ApplicationsByAumid.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TryPersistCache_SavesMetadata_ForEveryApp()
    {
        using var repository = CreateRepository(out var cacheStore);
        var apps = new[]
        {
            new InstalledApplicationInfo
            {
                AppUserModelId = "App.One",
                Name = "App One",
                TargetPath = @"C:\Apps\One.exe",
                Icon = Observable.Return((Bitmap?)null),
            },
        };

        repository.TryPersistCache(apps);

        await Assert.That(cacheStore.SavedEntries).IsNotNull();
        await Assert.That(cacheStore.SavedEntries!.Count).IsEqualTo(1);
        await Assert.That(cacheStore.SavedEntries![0].AppUserModelId).IsEqualTo("App.One");
        await Assert.That(cacheStore.SavedEntries![0].TargetPath).IsEqualTo(@"C:\Apps\One.exe");
    }

    [Test]
    public async Task TryPersistCache_EncodesIconAsPng_ForANewApp()
    {
        using var repository = CreateRepository(out var cacheStore);
        using var bitmap = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 1, 2, 3));
        var apps = new[]
        {
            new InstalledApplicationInfo
            {
                AppUserModelId = "App.WithIcon",
                Name = "With Icon",
                Icon = Observable.Return<Bitmap?>(bitmap),
            },
        };

        repository.TryPersistCache(apps);

        var bytes = cacheStore.SavedIconBytesByAumid!["App.WithIcon"];
        await Assert.That(bytes).IsNotNull();
        using var decoded = new Bitmap(new MemoryStream(bytes!));
        await Assert.That(decoded.GetPixel(0, 0)).IsEqualTo(Color.FromArgb(255, 1, 2, 3));
    }

    [Test]
    public async Task TryPersistCache_ReusesPreviousIconBytes_WhenMetadataIsUnchanged()
    {
        var previousEntry = new CachedApplicationEntry
        {
            AppUserModelId = "App.Unchanged",
            Name = "Unchanged",
            TargetPath = @"C:\Apps\Unchanged.exe",
        };
        using var repository = CreateRepository([previousEntry], out var cacheStore);
        cacheStore.IconBytesByAumid["App.Unchanged"] = [9, 9, 9];
        var iconWasSubscribedTo = false;
        var apps = new[]
        {
            new InstalledApplicationInfo
            {
                AppUserModelId = "App.Unchanged",
                Name = "Unchanged",
                TargetPath = @"C:\Apps\Unchanged.exe",
                // TryPersistCache swallows any exception from Icon (see its catch block), so a test
                // that throws here would still fail -- just on the byte assertion below, not on this
                // observable. A subscription flag is the airtight way to prove the carry-forward
                // branch never touches Icon for an app whose metadata didn't change.
                Icon = Observable.Defer(() =>
                {
                    iconWasSubscribedTo = true;
                    return Observable.Return((Bitmap?)null);
                }),
            },
        };

        repository.TryPersistCache(apps);

        await Assert.That(iconWasSubscribedTo).IsFalse();
        await Assert.That(cacheStore.SavedIconBytesByAumid!["App.Unchanged"]).IsEquivalentTo(new byte[] { 9, 9, 9 });
    }

    [Test]
    public async Task TryPersistCache_ReEncodesIcon_WhenMetadataChanged()
    {
        var previousEntry = new CachedApplicationEntry
        {
            AppUserModelId = "App.Moved",
            Name = "Moved",
            TargetPath = @"C:\Old\Path.exe",
        };
        using var repository = CreateRepository([previousEntry], out var cacheStore);
        cacheStore.IconBytesByAumid["App.Moved"] = [9, 9, 9];
        using var bitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        var apps = new[]
        {
            new InstalledApplicationInfo
            {
                AppUserModelId = "App.Moved",
                Name = "Moved",
                TargetPath = @"C:\New\Path.exe", // changed
                Icon = Observable.Return<Bitmap?>(bitmap),
            },
        };

        repository.TryPersistCache(apps);

        // Not the carried-forward [9, 9, 9] -- proves a changed TargetPath forces re-encoding.
        await Assert.That(cacheStore.SavedIconBytesByAumid!["App.Moved"]).IsNotEquivalentTo(new byte[] { 9, 9, 9 });
    }

    [Test]
    public async Task TryPersistCache_MapsToNullBytes_WhenIconObservableErrors()
    {
        using var repository = CreateRepository(out var cacheStore);
        var apps = new[]
        {
            new InstalledApplicationInfo
            {
                AppUserModelId = "App.BadIcon",
                Name = "Bad Icon",
                Icon = Observable.Throw<Bitmap?>(new InvalidOperationException("icon fetch failed")),
            },
        };

        repository.TryPersistCache(apps);

        await Assert.That(cacheStore.SavedIconBytesByAumid!["App.BadIcon"]).IsNull();
    }
}
```

Second, extend the existing repository test file with the seeding and cache-survives-a-failure tests, and update its existing test's constructor call:

```csharp
using DynamicData;
using WinTabber.Api.Media.ShellApplications.Caching;
using WinTabber.Api.Media.ShellApplications.Repositories;
using WinTabber.Api.Media.Tests.Fakes;

namespace WinTabber.Api.Media.Tests.ShellApplications;

public class InstalledApplicationRepositoryTests
{
    [Test]
    public async Task AcquisitionErrors_Emits_WhenAppsFolderAcquisitionFails()
    {
        var source = new FakeShellApplicationSource(() =>
            throw new InvalidOperationException("Shell unavailable")
        );
        using var repository = new InstalledApplicationRepository(source, new FakeInstalledApplicationCacheStore());

        var errorTcs = new TaskCompletionSource<Exception>();
        using var errorSubscription = repository.AcquisitionErrors.Subscribe(ex =>
            errorTcs.TrySetResult(ex)
        );

        var receivedCount = 0;
        var cacheErrored = false;
        using var subscription = repository.ApplicationsByAumid.Connect()
            .Subscribe(changes => receivedCount += changes.Count, _ => cacheErrored = true);

        var error = await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(receivedCount).IsEqualTo(0);
        await Assert.That(cacheErrored).IsFalse();
        await Assert.That(repository.ApplicationsByAumid.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ApplicationsByAumid_IsSeededFromCache_BeforeShellAcquisitionCompletes()
    {
        var cachedEntry = new CachedApplicationEntry { AppUserModelId = "App.Cached", Name = "Cached App" };
        var cacheStore = new FakeInstalledApplicationCacheStore([cachedEntry]);
        var source = new FakeShellApplicationSource(() =>
            throw new InvalidOperationException("Shell unavailable")
        );

        using var repository = new InstalledApplicationRepository(source, cacheStore);

        // The cache seed is applied synchronously during construction (a plain _apps.AddOrUpdate
        // call), before the live Shell scan -- which runs on the task pool, asynchronously -- has any
        // chance to run. No wait is needed here.
        await Assert.That(repository.ApplicationsByAumid.Count).IsEqualTo(1);
        await Assert.That(repository.ApplicationsByAumid.Lookup("App.Cached").HasValue).IsTrue();
    }

    [Test]
    public async Task ApplicationsByAumid_KeepsCachedEntries_WhenShellAcquisitionFails()
    {
        var cachedEntry = new CachedApplicationEntry { AppUserModelId = "App.Cached", Name = "Cached App" };
        var cacheStore = new FakeInstalledApplicationCacheStore([cachedEntry]);
        var source = new FakeShellApplicationSource(() =>
            throw new InvalidOperationException("Shell unavailable")
        );
        using var repository = new InstalledApplicationRepository(source, cacheStore);

        var errorTcs = new TaskCompletionSource<Exception>();
        using var errorSubscription = repository.AcquisitionErrors.Subscribe(ex =>
            errorTcs.TrySetResult(ex)
        );

        await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // A failed live scan must not clear out what the cache already seeded.
        await Assert.That(repository.ApplicationsByAumid.Count).IsEqualTo(1);
        await Assert.That(repository.ApplicationsByAumid.Lookup("App.Cached").HasValue).IsTrue();
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test WinTabber.Api.Media.Tests -- --treenode-filter "/*/*/InstalledApplicationRepository*Tests/*"`
Expected: FAIL to compile — `InstalledApplicationRepository` does not yet take a second constructor parameter, and `MergeFreshApps`/`TryPersistCache` do not exist or are not accessible.

- [ ] **Step 4: Add `InternalsVisibleTo` so the tests can call the internal methods**

In `WinTabber.Api.Media\WinTabber.Api.Media.csproj`, inside the existing `<ItemGroup>` that has `<ProjectReference>`:

```xml
        <ProjectReference Include="..\WinTabber.Common.Util\WinTabber.Common.Util.csproj" />
		<ProjectReference Include="..\WinTabber.Generators\WinTabber.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    </ItemGroup>

    <ItemGroup>
        <InternalsVisibleTo Include="WinTabber.Api.Media.Tests" />
    </ItemGroup>
```

(Add the new `<ItemGroup>` right after the existing one, so the pre-existing block is untouched.)

- [ ] **Step 5: Modify `InstalledApplicationRepository`**

Add these `using` statements to the top of the file:

```csharp
using System.Drawing.Imaging;
using WinTabber.Api.Media.ShellApplications.Caching;
```

Add a field for the cache store and a field for the previously-cached entries, next to the existing fields:

```csharp
    private readonly IShellApplicationSource _shellSource;
    private readonly IInstalledApplicationCacheStore _cacheStore;
    private readonly SourceCache<InstalledApplicationInfo, string> _apps = new(static app =>
        app.AppUserModelId
    );
    private IReadOnlyDictionary<string, CachedApplicationEntry> _previousEntriesByAumid =
        new Dictionary<string, CachedApplicationEntry>();
    private readonly IDisposable _liveAcquisitionSubscription;
```

`_previousEntriesByAumid` is set once in the constructor (via `LoadCachedApplications`, below) and read from the background persistence path — never reassigned after that, so there is no cross-thread write to synchronize.

(`_apps` already existed in the class but was never read from or written to — every line above involving it in the constructor below is new; the field itself was already there.)

Replace the constructor body:

```csharp
    public InstalledApplicationRepository(IShellApplicationSource shellSource)
    {
        _shellSource = shellSource;
        //var primaryAumidCache = GetRefreshEvents()
        //    .StartWith(Unit.Default)
        //    .ExhaustMap(_ => GetInstalledApplicationsObservable())
        // DynamicData's Or() combinator (used below to merge this cache with its derived
        // partial/package/target caches) silently drops an OnError from its source instead of
        // propagating it to Connect() subscribers — confirmed with a reduced repro independent of
        // this class's own composition, not just an artifact of subscribing to the same cold
        // source multiple times. So a failure is caught here, before it ever reaches Or(), and
        // reported on AcquisitionErrors instead; the changeset itself completes as if acquisition
        // returned an empty list, keeping Or()/AutoRefreshOnObservable on their normal path.
        // Publish().RefCount() then shares that one execution across every downstream subscriber
        // (Or() directly, AutoRefreshOnObservable, and the partial/package/target caches derived
        // from it) so a failure is reported once, not once per subscriber. Proven by
        // InstalledApplicationRepositoryTests.AcquisitionErrors_Emits_WhenAppsFolderAcquisitionFails.
        var primaryAumidCache = GetInstalledApplicationsObservable()
            .Catch<IReadOnlyList<InstalledApplicationInfo>, Exception>(ex =>
            {
                _acquisitionErrors.OnNext(ex);
                return Observable.Return<IReadOnlyList<InstalledApplicationInfo>>([]);
            })
            .ToObservableChangeSet(
                keySelector: app => app.AppUserModelId,
                expireAfter: item => TimeSpan.FromDays(1)
            )
            .Publish()
            .RefCount();
```

with:

```csharp
    public InstalledApplicationRepository(IShellApplicationSource shellSource, IInstalledApplicationCacheStore cacheStore)
    {
        _shellSource = shellSource;
        _cacheStore = cacheStore;

        // Seeded synchronously -- a small JSON read, fast enough not to delay construction -- so
        // ApplicationsByAumid/ApplicationsByPath have content immediately, before the live Shell
        // scan below (which runs on the task pool, in the background) produces anything.
        _apps.AddOrUpdate(LoadCachedApplications());

        // DynamicData's Or() combinator (used below to merge this cache with its derived
        // partial/package/target caches) silently drops an OnError from its source instead of
        // propagating it to Connect() subscribers — confirmed with a reduced repro independent of
        // this class's own composition, not just an artifact of subscribing to the same cold
        // source multiple times. That risk no longer applies to _apps.Connect() itself: _apps is a
        // plain mutable cache that never errors, and every failure path below is fully absorbed by
        // Catch before this repository's own Subscribe -- Or() never sees it. AcquisitionErrors is
        // still how a consumer learns about it. Proven by
        // InstalledApplicationRepositoryTests.AcquisitionErrors_Emits_WhenAppsFolderAcquisitionFails.
        var primaryAumidCache = _apps.Connect().Publish().RefCount();

        // MergeFreshApps and TryPersistCache are NOT run inline with this scan -- MergeFreshApps
        // (fast, in-memory) runs directly here; TryPersistCache (icon PNG-encoding, disk I/O) is
        // pushed to Task.Run inside MergeFreshApps so a slow persist never delays the changeset this
        // scan just produced from reaching ApplicationsByAumid's subscribers.
        //
        // A failed scan (the Catch below) reports on AcquisitionErrors and produces no further
        // action -- deliberately not a call to MergeFreshApps([]), which would otherwise remove
        // every cache-seeded entry (MergeFreshApps treats "not present in the fresh list" as
        // "uninstalled"). Proven by
        // InstalledApplicationRepositoryTests.ApplicationsByAumid_KeepsCachedEntries_WhenShellAcquisitionFails.
        _liveAcquisitionSubscription = GetInstalledApplicationsObservable()
            .Catch<IReadOnlyList<InstalledApplicationInfo>, Exception>(ex =>
            {
                _acquisitionErrors.OnNext(ex);
                return Observable.Empty<IReadOnlyList<InstalledApplicationInfo>>();
            })
            .Subscribe(MergeFreshApps);
```

The rest of the constructor (the `partialAumidCache`/`packagePathCache`/etc. block through `ApplicationsByPath = ...`) is unchanged.

Now update `Dispose()`. Replace:

```csharp
    public void Dispose()
    {
        ApplicationsByAumid.Dispose();
        ApplicationsByPath.Dispose();
        _acquisitionErrors.Dispose();
    }
```

with:

```csharp
    public void Dispose()
    {
        _liveAcquisitionSubscription.Dispose();
        ApplicationsByAumid.Dispose();
        ApplicationsByPath.Dispose();
        _apps.Dispose();
        _acquisitionErrors.Dispose();
    }
```

Now add these new members, placed after `GetInstalledApplicationsObservable()`:

```csharp
    private IReadOnlyList<InstalledApplicationInfo> LoadCachedApplications()
    {
        IReadOnlyList<CachedApplicationEntry> entries;
        try
        {
            entries = _cacheStore.Load();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load installed-application cache: {ex.Message}");
            entries = [];
        }

        _previousEntriesByAumid = entries.ToDictionary(entry => entry.AppUserModelId);

        return entries
            .Select(entry => new InstalledApplicationInfo
            {
                AppUserModelId = entry.AppUserModelId,
                Name = entry.Name,
                TargetPath = entry.TargetPath,
                PackageInstallPath = entry.PackageInstallPath,
                Icon = GetCachedIcon(entry),
            })
            .ToArray();
    }

    private IObservable<Bitmap?> GetCachedIcon(CachedApplicationEntry entry)
    {
        return Observable
            .Defer(() => Observable.Start(() => _cacheStore.LoadIcon(entry), TaskPoolScheduler.Default))
            .Replay(1)
            .AutoConnect();
    }

    /// <summary>
    /// Replaces <see cref="_apps"/>'s contents with <paramref name="freshApps"/>: an app not in
    /// <paramref name="freshApps"/> is removed (it is no longer installed), an app already present
    /// is overwritten with the fresh value regardless of <see cref="InstalledApplicationInfo.Equals"/>
    /// (which compares only the AUMID, so it cannot be relied on to detect a changed Icon/TargetPath/Name),
    /// and a new app is added. Deliberately not built on <c>ToObservableChangeSet</c> over a
    /// concatenated cached-then-fresh sequence -- that operator was verified (see the plan's "Design
    /// decisions verified empirically" section) to be additive only, never removing a key absent from
    /// a later emission, which would leave an uninstalled app in the cache until its 1-day expiry.
    /// Internal so a test can drive it directly without needing a real successful Shell scan (not
    /// producible from a test -- <c>ShellObject</c> has no accessible constructor; see this project's
    /// README.md).
    /// </summary>
    internal void MergeFreshApps(IReadOnlyList<InstalledApplicationInfo> freshApps)
    {
        var freshKeys = new HashSet<string>(freshApps.Select(app => app.AppUserModelId), StringComparer.Ordinal);
        _apps.Edit(updater =>
        {
            var staleKeys = updater.Keys.Where(key => !freshKeys.Contains(key)).ToArray();
            updater.Remove(staleKeys);
            updater.AddOrUpdate(freshApps);
        });

        if (freshApps.Count > 0)
        {
            _ = Task.Run(() => TryPersistCache(freshApps));
        }
    }

    /// <summary>
    /// Internal so a test can call it directly and synchronously, instead of through the
    /// fire-and-forget <see cref="Task.Run(Action)"/> <see cref="MergeFreshApps"/> wraps it in.
    /// </summary>
    internal void TryPersistCache(IReadOnlyList<InstalledApplicationInfo> freshApps)
    {
        try
        {
            var entries = new List<CachedApplicationEntry>(freshApps.Count);
            var iconBytesByAumid = new Dictionary<string, byte[]?>(freshApps.Count);

            foreach (var app in freshApps)
            {
                entries.Add(new CachedApplicationEntry
                {
                    AppUserModelId = app.AppUserModelId,
                    Name = app.Name,
                    TargetPath = app.TargetPath,
                    PackageInstallPath = app.PackageInstallPath,
                });

                if (
                    _previousEntriesByAumid.TryGetValue(app.AppUserModelId, out var previous)
                    && previous.Name == app.Name
                    && previous.TargetPath == app.TargetPath
                    && previous.PackageInstallPath == app.PackageInstallPath
                )
                {
                    // Unchanged since the last successful write -- reuse the icon bytes already on
                    // disk instead of forcing GetIcon's COM extraction to run again. Without this,
                    // every launch would eagerly decode every installed app's icon up front, turning
                    // today's lazy, on-demand load into a full-catalog decode -- the opposite of what
                    // this cache exists to avoid.
                    iconBytesByAumid[app.AppUserModelId] = _cacheStore.LoadIconBytes(previous);
                }
                else
                {
                    iconBytesByAumid[app.AppUserModelId] = TryEncodeIcon(app.Icon);
                }
            }

            _cacheStore.Save(entries, iconBytesByAumid);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to persist installed-application cache: {ex.Message}");
        }
    }

    private static byte[]? TryEncodeIcon(IObservable<Bitmap?> icon)
    {
        try
        {
            var bitmap = icon.Timeout(TimeSpan.FromSeconds(5)).FirstOrDefaultAsync().Wait();
            if (bitmap is null)
            {
                return null;
            }

            using var memoryStream = new MemoryStream();
            bitmap.Save(memoryStream, ImageFormat.Png);
            return memoryStream.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test WinTabber.Api.Media.Tests -- --treenode-filter "/*/*/InstalledApplicationRepository*Tests/*"`
Expected: PASS (3 tests in `InstalledApplicationRepositoryTests` + 7 in `InstalledApplicationRepositoryPersistenceTests`)

- [ ] **Step 7: Run the full `WinTabber.Api.Media.Tests` project to check for regressions**

Run: `dotnet test WinTabber.Api.Media.Tests`
Expected: PASS, all tests including `SMTC`/`CoreAudio` tests untouched by this change.

- [ ] **Step 8: Update `WinTabber.Api.Media.Tests\README.md`**

Add a bullet after the existing `InstalledApplicationRepository` bullet (before "**Not covered, and why:**"):

```markdown
- `InstalledApplicationRepository`'s disk-cache seeding, merge, and persistence logic — via
  `Fakes/FakeShellApplicationSource.cs` and `ShellApplications/Caching/FakeInstalledApplicationCacheStore.cs`:
  a cache seed appears in `ApplicationsByAumid` immediately (synchronously, before the live Shell
  scan even starts), and survives a failed live scan rather than being cleared. The merge
  (`InstalledApplicationRepository.MergeFreshApps`) and cache-write (`TryPersistCache`) logic that a
  successful scan would otherwise drive are made `internal` so tests can call them directly with a
  plain `InstalledApplicationInfo` list, sidestepping the same no-accessible-constructor limitation
  `ShellObject` has everywhere else in this project — see
  `ShellApplications/InstalledApplicationRepositoryPersistenceTests.cs`. The file-backed store itself
  (`FileInstalledApplicationCacheStore`) is covered in
  `ShellApplications/Caching/FileInstalledApplicationCacheStoreTests.cs`.
```

- [ ] **Step 9: Commit**

```bash
git add WinTabber.Api.Media/ShellApplications/Repositories/InstalledApplicationRepository.cs \
        WinTabber.Api.Media/WinTabber.Api.Media.csproj \
        WinTabber.Api.Media.Tests/ShellApplications/InstalledApplicationRepositoryTests.cs \
        WinTabber.Api.Media.Tests/ShellApplications/InstalledApplicationRepositoryPersistenceTests.cs \
        WinTabber.Api.Media.Tests/ShellApplications/Caching/FakeInstalledApplicationCacheStore.cs \
        WinTabber.Api.Media.Tests/README.md
git commit -m "feat: seed InstalledApplicationRepository from disk cache and persist scan results"
```

---

### Task 3: Wire DI in both apps, delete `AppCache`

**Files:**
- Modify: `WinTabberUI\Bootstrapper.cs`
- Modify: `WinTabberUI\BackgroundServiceContainer.cs`
- Modify: `winui3\WinTabberUI\Bootstrapper.cs`
- Modify: `winui3\WinTabberUI\BackgroundServiceContainer.cs`
- Modify: `WinTabber.Infrastructure\WinTabber.Infrastructure.csproj`
- Delete: `WinTabber.Infrastructure\AppCache.cs`

**Interfaces:**
- Consumes: `FileInstalledApplicationCacheStore`, `IInstalledApplicationCacheStore` from Task 1.
- Consumes: `InstalledApplicationRepository(IShellApplicationSource, IInstalledApplicationCacheStore)` from Task 2.
- Consumes: `Paths.RoamingDataPath` (`WinTabber.Infrastructure\Paths.cs`, namespace `WinTabberUI`, already in scope in both Bootstrapper files without a new `using` — both files are themselves `namespace WinTabberUI`, confirmed by reading both during planning).

- [ ] **Step 1: Update `WinTabberUI\Bootstrapper.cs`**

Add to the `using` block (alphabetically, alongside the other `WinTabber.Api.Media.ShellApplications` usings):

```csharp
using WinTabber.Api.Media.ShellApplications.Caching;
```

Also add, if not already present:

```csharp
using System.IO;
```

Replace:

```csharp
            .AddSingleton<IShellApplicationSource, WindowsShellApplicationSource>()
            .AddSingleton<IInstalledApplicationRepository, InstalledApplicationRepository>();
```

with:

```csharp
            .AddSingleton<IInstalledApplicationCacheStore>(
                _ => new FileInstalledApplicationCacheStore(Path.Combine(Paths.RoamingDataPath, "InstalledApplications"))
            )
            .AddSingleton<IShellApplicationSource, WindowsShellApplicationSource>()
            .AddSingleton<IInstalledApplicationRepository, InstalledApplicationRepository>();
```

Delete the line:

```csharp
            .AddSingleton<AppCache>()
```

- [ ] **Step 2: Update `WinTabberUI\BackgroundServiceContainer.cs`**

Delete the line:

```csharp
        ioc.GetRequiredService<AppCache>().Load();
```

Check whether any other line in this file still uses a type from `WinTabberUI.Infrastructure` before removing the `using WinTabberUI.Infrastructure;` line — `AppCache` was the only type from that namespace referenced in this file at planning time, but confirm against the file as it stands when this step actually runs.

- [ ] **Step 3: Update `winui3\WinTabberUI\Bootstrapper.cs`**

Apply the same change as Step 1, to the equivalent lines in this file (same line shapes, confirmed identical in both files during planning: `.AddSingleton<AppCache>()` and the `IShellApplicationSource`/`IInstalledApplicationRepository` pair).

- [ ] **Step 4: Update `winui3\WinTabberUI\BackgroundServiceContainer.cs`**

Delete the line:

```csharp
        ioc.GetRequiredService<AppCache>().Load();
```

Also update the class doc-comment above the constructor, which names `AppCache` as one of the things this container preloads. Replace:

```csharp
/// <summary>
/// Ported from the WPF app's own <c>BackgroundServiceContainer</c>. Preloads the same shared state
/// (<see cref="AppCache"/>, <see cref="WindowManager"/>, <see cref="ApplicationStateViewModel"/>)
```

with:

```csharp
/// <summary>
/// Ported from the WPF app's own <c>BackgroundServiceContainer</c>. Preloads the same shared state
/// (<see cref="WindowManager"/>, <see cref="ApplicationStateViewModel"/>)
```

Same check as Step 2: confirm no other type from `WinTabberUI.Infrastructure` is used in this file before removing its `using`.

- [ ] **Step 5: Delete `AppCache` and its now-unused package reference**

```bash
git rm WinTabber.Infrastructure/AppCache.cs
```

In `WinTabber.Infrastructure\WinTabber.Infrastructure.csproj`, remove the line:

```xml
        <PackageReference Include="System.Runtime.Caching" />
```

`AppCache` was this package's only consumer in the project (it used `System.Runtime.Caching.MemoryCache`) — confirm nothing else in `WinTabber.Infrastructure` references `System.Runtime.Caching` before removing the line.

- [ ] **Step 6: Build both apps**

Run: `dotnet build WinTabber.slnx`
Expected: Build succeeds with no errors. (`AppCache`'s removal must not leave any other reference — Task 3 removed every call site found during planning; a leftover reference here means one was missed.)

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test --solution WinTabber.slnx`
Expected: PASS across all test projects.

- [ ] **Step 8: Manually verify app-picker startup behavior**

Launch the WPF app (`dotnet run --project WinTabberUI/WinTabberUI.csproj`) with media controls enabled, open the app picker, confirm it populates. Close and relaunch; confirm `%APPDATA%\WinTabber\InstalledApplications\installed-apps.json` and `installed-apps.icons` now exist, and that the app picker's icons still render correctly on this second launch (proving the cached-icon read path works, not just the live one). Then uninstall or rename a shortcut for one listed app, relaunch a third time, and confirm that app no longer appears (proving the removal half of `MergeFreshApps` works against a real scan, not just the unit tests).

- [ ] **Step 9: Commit**

```bash
git add WinTabberUI/Bootstrapper.cs WinTabberUI/BackgroundServiceContainer.cs \
        winui3/WinTabberUI/Bootstrapper.cs winui3/WinTabberUI/BackgroundServiceContainer.cs \
        WinTabber.Infrastructure/WinTabber.Infrastructure.csproj
git rm WinTabber.Infrastructure/AppCache.cs
git commit -m "chore: remove dead AppCache, wire installed-application cache store into both apps"
```

---

## Self-Review Notes

- **Spec coverage:** cache read at startup (Task 2, `LoadCachedApplications`) — done; merge fresh over cached with correct removal of uninstalled apps (Task 2, `MergeFreshApps`, verified empirically against the additive behavior of the alternative `ToObservableChangeSet` approach) — done; write-back after scan completes, without eagerly re-decoding unchanged icons (Task 2, `TryPersistCache`) — done; icon storage as metadata-JSON-plus-combined-icon-blob (Task 1) — done; safe against a concurrent read during write-back (Task 1, temp-file-then-move plus `FileShare.ReadWrite | FileShare.Delete`) — done; `AppCache` disposition (Task 3, deleted, including its now-orphaned `System.Runtime.Caching` package reference) — done. The pre-existing 1-day `expireAfter` is deliberately dropped rather than ported forward: it would conflict with `MergeFreshApps`'s own, more correct removal, and — since `GetInstalledApplicationsObservable()` runs exactly once per process — a background expiry timer would have no scan left to repopulate an entry it dropped.
- **Placeholder scan:** no TBDs; every step has concrete code. The "confirm no other reference" checks in Task 3, Steps 2, 4, and 5 are phrased as a check-then-act rather than fixed code because the exact contents of each file/project at execution time can only be confirmed then — that is a real verification step, not a missing detail; at planning time each was independently confirmed to have exactly one such reference.
- **Type consistency:** `IInstalledApplicationCacheStore.Save`'s `iconBytesByAumid` parameter type (`IReadOnlyDictionary<string, byte[]?>`) matches what `TryPersistCache` builds and what `FakeInstalledApplicationCacheStore.Save` and `FileInstalledApplicationCacheStore.Save` both accept. `CachedApplicationEntry` field names match between Task 1's definition and every consumer in Task 2. `MergeFreshApps`/`TryPersistCache` are both `internal` on the same class Task 1's `IInstalledApplicationCacheStore` is consumed by, matching the `InternalsVisibleTo` added in Task 2, Step 4.
- **Verified against the actual installed package, not assumed:** the "Design decisions verified empirically" section's two claims about DynamicData 9.4.1's `ToObservableChangeSet` overload, and the `SourceCache.Edit`/`AddOrUpdate`/`Remove` merge semantics Task 2 relies on instead, were each checked by running a small throwaway console program against `~/.nuget/packages/dynamicdata/9.4.1` during planning, not inferred from general familiarity with the library.
