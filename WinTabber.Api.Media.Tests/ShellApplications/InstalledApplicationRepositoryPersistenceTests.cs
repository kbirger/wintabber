using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using WinTabber.Api.Media.ShellApplications.Caching;
using WinTabber.Api.Media.ShellApplications.Models;
using WinTabber.Api.Media.ShellApplications.Repositories;
using WinTabber.Api.Media.Tests.Fakes;
using WinTabber.Api.Media.Tests.ShellApplications.Caching;

namespace WinTabber.Api.Media.Tests.ShellApplications;

public class InstalledApplicationRepositoryPersistenceTests
{
    // Split into two overloads rather than a single method with an optional parameter before the
    // out parameter -- C# requires optional parameters to appear after all required ones (CS1737),
    // and out parameters can't carry a default value, so (seedEntries = null, out cacheStore) isn't
    // legal as one signature. This preserves both calling shapes the tests below use.
    private static InstalledApplicationRepository CreateRepository(
        out FakeInstalledApplicationCacheStore cacheStore
    ) => CreateRepository(null, out cacheStore);

    private static InstalledApplicationRepository CreateRepository(
        IReadOnlyList<CachedApplicationEntry>? seedEntries,
        out FakeInstalledApplicationCacheStore cacheStore
    )
    {
        cacheStore = new FakeInstalledApplicationCacheStore(seedEntries);
        var source = new FakeShellApplicationSource(() =>
            throw new InvalidOperationException("Not exercised by these tests")
        );
        return new InstalledApplicationRepository(source, cacheStore, TimeSpan.Zero);
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

        // An empty fresh list is untrustworthy (no later scan could ever fix a mistakenly-emptied
        // cache), so MergeFreshApps must leave the cache-seeded entry alone rather than remove it.
        await Assert.That(repository.ApplicationsByAumid.Count).IsEqualTo(1);
        await Assert.That(repository.ApplicationsByAumid.Lookup("App.Uninstalled").HasValue).IsTrue();
    }

    [Test]
    public async Task MergeFreshApps_RemovesOnlyTheUninstalledApp_WhenTheOtherSurvives()
    {
        using var repository = CreateRepository(
            [
                new CachedApplicationEntry { AppUserModelId = "App.Uninstalled", Name = "Gone" },
                new CachedApplicationEntry { AppUserModelId = "App.Survivor", Name = "Still Here" },
            ],
            out _
        );

        repository.MergeFreshApps([
            new InstalledApplicationInfo
            {
                AppUserModelId = "App.Survivor",
                Name = "Still Here",
                Icon = Observable.Return((Bitmap?)null),
            },
        ]);

        await Assert.That(repository.ApplicationsByAumid.Count).IsEqualTo(1);
        await Assert.That(repository.ApplicationsByAumid.Lookup("App.Survivor").HasValue).IsTrue();
        await Assert.That(repository.ApplicationsByAumid.Lookup("App.Uninstalled").HasValue).IsFalse();
    }

    [Test]
    public async Task Constructor_DoesNotThrow_WhenCacheStoreReturnsDuplicateAumidEntries()
    {
        using var repository = CreateRepository(
            [
                new CachedApplicationEntry { AppUserModelId = "App.Dup", Name = "First" },
                new CachedApplicationEntry { AppUserModelId = "App.Dup", Name = "Second" },
            ],
            out _
        );

        await Assert.That(repository.ApplicationsByAumid.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TryPersistCache_DoesNotThrow_AndDedupesEntries_WhenFreshAppsShareAnAumid()
    {
        using var repository = CreateRepository(out var cacheStore);
        var apps = new[]
        {
            new InstalledApplicationInfo
            {
                AppUserModelId = "App.Dup",
                Name = "Per-User Shortcut",
                TargetPath = @"C:\Users\someone\App.exe",
                Icon = Observable.Return((Bitmap?)null),
            },
            new InstalledApplicationInfo
            {
                AppUserModelId = "App.Dup",
                Name = "Per-Machine Shortcut",
                TargetPath = @"C:\ProgramData\App.exe",
                Icon = Observable.Return((Bitmap?)null),
            },
        };

        repository.TryPersistCache(apps);
        await repository.FlushCacheAsync();

        await Assert.That(cacheStore.SavedEntries).IsNotNull();
        await Assert.That(cacheStore.SavedEntries!.Count(entry => entry.AppUserModelId == "App.Dup")).IsEqualTo(1);
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
        await repository.FlushCacheAsync();

        await Assert.That(cacheStore.SavedEntries).IsNotNull();
        await Assert.That(cacheStore.SavedEntries!.Count).IsEqualTo(1);
        await Assert.That(cacheStore.SavedEntries![0].AppUserModelId).IsEqualTo("App.One");
        await Assert.That(cacheStore.SavedEntries![0].TargetPath).IsEqualTo(@"C:\Apps\One.exe");
    }

    [Test]
    public async Task TryPersistCache_DoesNotSubscribeToAnyIcon()
    {
        var iconWasSubscribedTo = false;
        using var repository = CreateRepository(out var cacheStore);
        var apps = new[]
        {
            new InstalledApplicationInfo
            {
                AppUserModelId = "App.Lazy",
                Name = "Lazy",
                TargetPath = @"C:\Apps\Lazy.exe",
                // Persisting the catalog must not decode a single icon: most installed apps never
                // have a media session, so an icon is extracted only when something asks for it.
                // A subscription flag, not a throw, is what fails this test -- the persist path
                // swallows exceptions.
                Icon = Observable.Defer(() =>
                {
                    iconWasSubscribedTo = true;
                    return Observable.Return((Bitmap?)null);
                }),
            },
        };

        repository.TryPersistCache(apps);
        await repository.FlushCacheAsync();

        await Assert.That(iconWasSubscribedTo).IsFalse();
        await Assert.That(cacheStore.SavedEntries!.Single().AppUserModelId).IsEqualTo("App.Lazy");
    }

    [Test]
    public async Task TryPersistCache_KeepsAnUnchangedAppsSavedIcon_WithoutTouchingIt()
    {
        var previousEntry = new CachedApplicationEntry
        {
            AppUserModelId = "App.Unchanged",
            Name = "Unchanged",
            TargetPath = @"C:\Apps\Unchanged.exe",
            IconOffset = 0,
            IconLength = 3,
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
                Icon = Observable.Defer(() =>
                {
                    iconWasSubscribedTo = true;
                    return Observable.Return((Bitmap?)null);
                }),
            },
            // A second, new app forces a real save, so the carry-forward is observable.
            new InstalledApplicationInfo
            {
                AppUserModelId = "App.New",
                Name = "New",
                Icon = Observable.Return((Bitmap?)null),
            },
        };

        repository.TryPersistCache(apps);
        await repository.FlushCacheAsync();

        await Assert.That(iconWasSubscribedTo).IsFalse();
        await Assert.That(cacheStore.SavedIconBytesByAumid!["App.Unchanged"]).IsEquivalentTo(new byte[] { 9, 9, 9 });
    }

    [Test]
    public async Task CreateIcon_ReadsTheCacheFirst_AndSkipsShell_WhenTheEntryIsUnchanged()
    {
        var previousEntry = new CachedApplicationEntry
        {
            AppUserModelId = "App.Cached",
            Name = "Cached",
            TargetPath = @"C:\Apps\Cached.exe",
            IconOffset = 0,
            IconLength = 3,
        };
        using var repository = CreateRepository([previousEntry], out var cacheStore);
        using var cachedBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        cacheStore.BitmapsByAumid["App.Cached"] = cachedBitmap;
        var shellWasSubscribedTo = false;
        var shell = Observable.Defer(() =>
        {
            shellWasSubscribedTo = true;
            return Observable.Return((Bitmap?)null);
        });

        var icon = repository.CreateIcon("App.Cached", "Cached", @"C:\Apps\Cached.exe", null, shell);
        var result = await icon.FirstAsync();

        await Assert.That(result).IsSameReferenceAs(cachedBitmap);
        await Assert.That(shellWasSubscribedTo).IsFalse();
    }

    [Test]
    public async Task CreateIcon_FallsBackToShell_WhenTheCacheHasNoIcon()
    {
        var previousEntry = new CachedApplicationEntry
        {
            AppUserModelId = "App.NoIcon",
            Name = "No Icon",
            TargetPath = @"C:\Apps\NoIcon.exe",
        };
        using var repository = CreateRepository([previousEntry], out var cacheStore);
        using var shellBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);

        var icon = repository.CreateIcon(
            "App.NoIcon",
            "No Icon",
            @"C:\Apps\NoIcon.exe",
            null,
            Observable.Return<Bitmap?>(shellBitmap)
        );
        var result = await icon.FirstAsync();

        await Assert.That(result).IsSameReferenceAs(shellBitmap);
        await Assert.That(cacheStore.LoadIconCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task CreateIcon_FallsBackToShell_WhenTheEntryChanged()
    {
        var previousEntry = new CachedApplicationEntry
        {
            AppUserModelId = "App.Moved",
            Name = "Moved",
            TargetPath = @"C:\Old\Path.exe",
            IconOffset = 0,
            IconLength = 3,
        };
        using var repository = CreateRepository([previousEntry], out var cacheStore);
        using var staleBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        using var shellBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        cacheStore.BitmapsByAumid["App.Moved"] = staleBitmap;

        var icon = repository.CreateIcon(
            "App.Moved",
            "Moved",
            @"C:\New\Path.exe",
            null,
            Observable.Return<Bitmap?>(shellBitmap)
        );
        var result = await icon.FirstAsync();

        await Assert.That(result).IsSameReferenceAs(shellBitmap);
    }

    [Test]
    public async Task CreateIcon_FallsBackToShell_WhenTheCachedIconCannotBeRead()
    {
        var previousEntry = new CachedApplicationEntry
        {
            AppUserModelId = "App.Corrupt",
            Name = "Corrupt",
            TargetPath = @"C:\Apps\Corrupt.exe",
            IconOffset = 0,
            IconLength = 3,
        };
        // BitmapsByAumid is left empty, so the fake's LoadIcon returns null -- the same signal the
        // real store gives for a corrupt or truncated blob.
        using var repository = CreateRepository([previousEntry], out _);
        using var shellBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);

        var icon = repository.CreateIcon(
            "App.Corrupt",
            "Corrupt",
            @"C:\Apps\Corrupt.exe",
            null,
            Observable.Return<Bitmap?>(shellBitmap)
        );

        await Assert.That(await icon.FirstAsync()).IsSameReferenceAs(shellBitmap);
    }

    [Test]
    public async Task CanHaveIcon_IsFalse_ForANullOrEmptyPath()
    {
        await Assert.That(InstalledApplicationRepository.CanHaveIcon(null)).IsFalse();
        await Assert.That(InstalledApplicationRepository.CanHaveIcon("")).IsFalse();
        await Assert.That(InstalledApplicationRepository.CanHaveIcon("   ")).IsFalse();
    }

    [Test]
    public async Task CanHaveIcon_IsFalse_ForAShellNamespacePath()
    {
        await Assert.That(InstalledApplicationRepository.CanHaveIcon("::{52205FD8-5DFB-447D-801A-D0B52F2E83E1}")).IsFalse();
    }

    [Test]
    public async Task CanHaveIcon_IsFalse_ForAPathThatDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Missing.exe");

        await Assert.That(InstalledApplicationRepository.CanHaveIcon(missing)).IsFalse();
    }

    [Test]
    public async Task CanHaveIcon_IsTrue_ForAnExistingFileAndAnExistingDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "App.exe");
        await File.WriteAllBytesAsync(file, [0]);
        try
        {
            await Assert.That(InstalledApplicationRepository.CanHaveIcon(file)).IsTrue();
            await Assert.That(InstalledApplicationRepository.CanHaveIcon(directory)).IsTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
