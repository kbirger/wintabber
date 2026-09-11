using DynamicData;
using WinTabber.Api.Media.ShellApplications.Repositories;
using WinTabber.Api.Media.Tests.Fakes;

namespace WinTabber.Api.Media.Tests.ShellApplications;

public class InstalledApplicationRepositoryTests
{
    // A prior version of this test subscribed to `ApplicationsByAumid.Connect()` and expected the
    // fake source's thrown exception to surface as `OnError` there. It never did: DynamicData's
    // `Or()` combinator — used to build `ApplicationsByAumid`/`ApplicationsByPath` — silently drops
    // an upstream `OnError`, confirmed with a reduced repro independent of this class's own
    // composition (not an artifact of subscribing to the same cold source multiple times; sharing
    // one execution via `Publish().RefCount()` did not change the outcome). The repository now
    // catches acquisition failures itself, before they reach `Or()`, and reports them on
    // `AcquisitionErrors` instead — this test asserts that signal, plus that the caches stay empty
    // and never themselves error.
    [Test]
    public async Task AcquisitionErrors_Emits_WhenAppsFolderAcquisitionFails()
    {
        var source = new FakeShellApplicationSource(() =>
            throw new InvalidOperationException("Shell unavailable")
        );
        using var repository = new InstalledApplicationRepository(source);

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
}
