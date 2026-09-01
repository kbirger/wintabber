using System.Reactive.Subjects;
using DynamicData;
using WinTabber.Api.Media.SMTC.Repositories;

namespace WinTabber.Infrastructure.Tests.Media;

/// <summary>
/// Covers the snapshot-to-change-set step in SMTCSessionRepository. The session manager reports the
/// full set of sessions on every change, so the media controls list must drop a session that is
/// gone.
/// </summary>
public class SmtcSessionChangeSetTests
{
    private sealed record FakeSession(string Aumid);

    private static IObservableCache<FakeSession, string> BuildCache(IObservable<IReadOnlyList<FakeSession>> snapshots)
    {
        return SMTCSessionRepository.ToSessionChangeSet(snapshots, session => session.Aumid).AsObservableCache();
    }

    [Test]
    public async Task A_session_that_leaves_the_snapshot_is_removed()
    {
        var snapshots = new Subject<IReadOnlyList<FakeSession>>();
        var spotify = new FakeSession("Spotify");
        var chrome = new FakeSession("Chrome");

        using var cache = BuildCache(snapshots);

        snapshots.OnNext([spotify, chrome]);
        await Assert.That(cache.Keys).IsEquivalentTo(new[] { "Spotify", "Chrome" });

        snapshots.OnNext([spotify]);
        await Assert.That(cache.Keys).IsEquivalentTo(new[] { "Spotify" });
    }

    [Test]
    public async Task An_empty_snapshot_clears_every_session()
    {
        var snapshots = new Subject<IReadOnlyList<FakeSession>>();

        using var cache = BuildCache(snapshots);

        snapshots.OnNext([new FakeSession("Spotify")]);
        await Assert.That(cache.Count).IsEqualTo(1);

        snapshots.OnNext([]);
        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task A_session_that_stays_is_kept_and_a_new_one_is_added()
    {
        var snapshots = new Subject<IReadOnlyList<FakeSession>>();
        var spotify = new FakeSession("Spotify");

        using var cache = BuildCache(snapshots);

        snapshots.OnNext([spotify]);
        snapshots.OnNext([spotify, new FakeSession("Chrome")]);

        await Assert.That(cache.Keys).IsEquivalentTo(new[] { "Spotify", "Chrome" });
    }
}
