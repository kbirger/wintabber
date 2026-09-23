using System.Drawing;
using System.Drawing.Imaging;
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
