using WinTabber.Api.Media.ShellApplications.Caching;

namespace WinTabber.Api.Media.Tests.ShellApplications.Caching;

public class InstalledApplicationCacheWriterTests
{
    // TimeSpan.Zero: no batching delay, so a test never waits on the real two-second window.
    private static InstalledApplicationCacheWriter CreateWriter(
        FakeInstalledApplicationCacheStore store,
        IReadOnlyList<CachedApplicationEntry>? initial = null,
        TimeSpan? flushInterval = null
    ) => new(store, initial ?? [], flushInterval ?? TimeSpan.Zero);

    [Test]
    public async Task TryQueueIcon_WritesTheIcon_OnFlush()
    {
        var store = new FakeInstalledApplicationCacheStore();
        using var writer = CreateWriter(store, [new CachedApplicationEntry { AppUserModelId = "App.One", Name = "One" }]);

        var accepted = writer.TryQueueIcon("App.One", [1, 2, 3]);
        await writer.FlushAsync();

        await Assert.That(accepted).IsTrue();
        await Assert.That(store.SavedIconBytesByAumid!["App.One"]).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(store.SavedEntries!.Single().AppUserModelId).IsEqualTo("App.One");
    }

    [Test]
    public async Task QueueMetadata_DoesNotSave_WhenNothingChanged()
    {
        var store = new FakeInstalledApplicationCacheStore();
        var existing = new CachedApplicationEntry
        {
            AppUserModelId = "App.One",
            Name = "One",
            TargetPath = @"C:\Apps\One.exe",
            IconOffset = 0,
            IconLength = 3,
        };
        using var writer = CreateWriter(store, [existing]);

        // A freshly scanned entry never carries icon offsets; only its metadata may be compared.
        writer.QueueMetadata([existing with { IconOffset = -1, IconLength = 0 }]);
        await writer.FlushAsync();

        await Assert.That(store.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task QueueMetadata_CarriesForwardTheIcon_OfAnUnchangedEntry()
    {
        var store = new FakeInstalledApplicationCacheStore();
        store.IconBytesByAumid["App.Unchanged"] = [9, 9, 9];
        var existing = new CachedApplicationEntry
        {
            AppUserModelId = "App.Unchanged",
            Name = "Unchanged",
            TargetPath = @"C:\Apps\Unchanged.exe",
            IconOffset = 0,
            IconLength = 3,
        };
        using var writer = CreateWriter(store, [existing]);

        writer.QueueMetadata([
            existing with { IconOffset = -1, IconLength = 0 },
            new CachedApplicationEntry { AppUserModelId = "App.New", Name = "New" },
        ]);
        await writer.FlushAsync();

        await Assert.That(store.SaveCount).IsEqualTo(1);
        await Assert.That(store.SavedIconBytesByAumid!["App.Unchanged"]).IsEquivalentTo(new byte[] { 9, 9, 9 });
    }

    [Test]
    public async Task QueueMetadata_DropsTheIcon_WhenTheEntryChanged()
    {
        var store = new FakeInstalledApplicationCacheStore();
        store.IconBytesByAumid["App.Moved"] = [9, 9, 9];
        var existing = new CachedApplicationEntry
        {
            AppUserModelId = "App.Moved",
            Name = "Moved",
            TargetPath = @"C:\Old\Path.exe",
            IconOffset = 0,
            IconLength = 3,
        };
        using var writer = CreateWriter(store, [existing]);

        writer.QueueMetadata([new CachedApplicationEntry
        {
            AppUserModelId = "App.Moved",
            Name = "Moved",
            TargetPath = @"C:\New\Path.exe",
        }]);
        await writer.FlushAsync();

        await Assert.That(store.SavedIconBytesByAumid!.ContainsKey("App.Moved")).IsFalse();
        await Assert.That(store.SavedEntries!.Single().IconLength).IsEqualTo(0);
    }

    [Test]
    public async Task ManyIcons_ArriveInOneBatch_AndProduceOneSave()
    {
        var store = new FakeInstalledApplicationCacheStore();
        var entries = Enumerable
            .Range(0, 5)
            .Select(i => new CachedApplicationEntry { AppUserModelId = $"App.{i}", Name = $"App {i}" })
            .ToArray();
        // A long interval holds the batch open; FlushAsync must still cut it short.
        using var writer = CreateWriter(store, entries, TimeSpan.FromMinutes(5));

        foreach (var entry in entries)
        {
            writer.TryQueueIcon(entry.AppUserModelId, [1]);
        }
        await writer.FlushAsync();

        await Assert.That(store.SaveCount).IsEqualTo(1);
        await Assert.That(store.SavedIconBytesByAumid!.Count).IsEqualTo(5);
    }

    [Test]
    public async Task Dispose_DrainsQueuedWrites_WithoutWaitingForTheBatchWindow()
    {
        var store = new FakeInstalledApplicationCacheStore();
        var writer = CreateWriter(
            store,
            [new CachedApplicationEntry { AppUserModelId = "App.One", Name = "One" }],
            TimeSpan.FromMinutes(5)
        );

        writer.TryQueueIcon("App.One", [4, 5]);
        writer.Dispose();

        await Assert.That(store.SavedIconBytesByAumid!["App.One"]).IsEquivalentTo(new byte[] { 4, 5 });
    }

    [Test]
    public async Task TryQueueIcon_ReturnsFalse_AfterDispose()
    {
        var store = new FakeInstalledApplicationCacheStore();
        var writer = CreateWriter(store);
        writer.Dispose();

        await Assert.That(writer.TryQueueIcon("App.One", [1])).IsFalse();
    }

    [Test]
    public async Task ASaveFailure_DoesNotEndTheWriter()
    {
        var store = new FakeInstalledApplicationCacheStore { ThrowOnNextSave = true };
        using var writer = CreateWriter(store, [new CachedApplicationEntry { AppUserModelId = "App.One", Name = "One" }]);

        writer.TryQueueIcon("App.One", [1]);
        await writer.FlushAsync();
        writer.TryQueueIcon("App.One", [2]);
        await writer.FlushAsync();

        await Assert.That(store.SavedIconBytesByAumid!["App.One"]).IsEquivalentTo(new byte[] { 2 });
    }
}
