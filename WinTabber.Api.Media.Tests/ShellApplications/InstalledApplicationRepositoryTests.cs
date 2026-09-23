using DynamicData;
using WinTabber.Api.Media.ShellApplications.Caching;
using WinTabber.Api.Media.ShellApplications.Repositories;
using WinTabber.Api.Media.Tests.Fakes;
using WinTabber.Api.Media.Tests.ShellApplications.Caching;

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
