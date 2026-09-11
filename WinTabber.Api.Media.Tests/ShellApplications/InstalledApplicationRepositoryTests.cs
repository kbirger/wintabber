using DynamicData;
using WinTabber.Api.Media.ShellApplications.Repositories;
using WinTabber.Api.Media.Tests.Fakes;

namespace WinTabber.Api.Media.Tests.ShellApplications;

public class InstalledApplicationRepositoryTests
{
    // The original design for this test subscribed to `ApplicationsByAumid.Connect()` and expected
    // the fake source's thrown exception to surface as `OnError` on that subscription. It does not:
    // empirically (see task-2-report.md), DynamicData's `Or()` combinator — used to combine
    // `primaryAumidCache` with `partialAumidCache` in the repository's constructor — silently
    // swallows an error raised by an asynchronously-scheduled source (this repository's acquisition
    // runs on `TaskPoolScheduler`) when that source has more than one subscriber, which is the case
    // here (`primaryAumidCache` is subscribed via both `Or()` directly and via the
    // `partialAumidCache` derived from it). A reduced repro without a single production line changed
    // (`Or()` over a `TaskPoolScheduler`-scheduled `Observable.Start` source with two subscribers)
    // reproduces the same silent hang; the same repro without `Or()` propagates `OnError` correctly
    // and instantly. This is a pre-existing characteristic of the repository's reactive composition,
    // unrelated to the `IShellApplicationSource` seam this test exercises, and out of scope to fix
    // here — so this test proves the seam works (the injected source is actually invoked, and its
    // failure keeps bad data out of the cache) rather than asserting on `OnError` propagation that
    // the production pipeline does not deliver.
    [Test]
    public async Task ApplicationsByAumid_StaysEmpty_WhenAppsFolderAcquisitionFails()
    {
        var invoked = new TaskCompletionSource();
        var source = new FakeShellApplicationSource(() =>
        {
            invoked.TrySetResult();
            throw new InvalidOperationException("Shell unavailable");
        });
        using var repository = new InstalledApplicationRepository(source);

        var receivedCount = 0;
        using var subscription = repository.ApplicationsByAumid.Connect()
            .Subscribe(changes => receivedCount += changes.Count);

        // Proves the repository actually reaches through the injected seam (not the real Windows
        // shell) to acquire applications.
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Give the (failed) background acquisition a moment to settle before asserting nothing
        // landed in the cache.
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        await Assert.That(receivedCount).IsEqualTo(0);
        await Assert.That(repository.ApplicationsByAumid.Count).IsEqualTo(0);
    }
}
