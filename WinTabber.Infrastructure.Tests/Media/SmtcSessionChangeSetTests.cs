using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using Microsoft.Reactive.Testing;
using WinTabber.Api.Media.SMTC.Repositories;

namespace WinTabber.Infrastructure.Tests.Media;

/// <summary>
/// Covers the snapshot-to-change-set step in SMTCSessionRepository. The session manager reports the
/// full set of sessions on every change, so the media controls list must drop a session that is
/// gone - but only once it has stayed gone for the removal delay, since a track skip removes and
/// re-adds a session within a few hundred milliseconds.
/// </summary>
public class SmtcSessionChangeSetTests
{
    private sealed record FakeSession(string Aumid, string Title = "");

    private static readonly TimeSpan RemovalDelay = TimeSpan.FromSeconds(1.5);

    private static IObservableCache<FakeSession, string> BuildCache(
        IObservable<IReadOnlyList<FakeSession>> snapshots,
        TestScheduler? scheduler = null
    )
    {
        return SMTCSessionRepository
            .ToSessionChangeSet(snapshots, session => session.Aumid, RemovalDelay, scheduler)
            .AsObservableCache();
    }

    [Test]
    public async Task A_session_that_leaves_the_snapshot_is_removed()
    {
        var scheduler = new TestScheduler();
        var snapshots = new Subject<IReadOnlyList<FakeSession>>();
        var spotify = new FakeSession("Spotify");
        var chrome = new FakeSession("Chrome");

        using var cache = BuildCache(snapshots, scheduler);

        snapshots.OnNext([spotify, chrome]);
        await Assert.That(cache.Keys).IsEquivalentTo(new[] { "Spotify", "Chrome" });

        snapshots.OnNext([spotify]);
        scheduler.AdvanceBy(RemovalDelay.Ticks + TimeSpan.FromSeconds(1).Ticks);
        await Assert.That(cache.Keys).IsEquivalentTo(new[] { "Spotify" });
    }

    [Test]
    public async Task An_empty_snapshot_clears_every_session()
    {
        var scheduler = new TestScheduler();
        var snapshots = new Subject<IReadOnlyList<FakeSession>>();

        using var cache = BuildCache(snapshots, scheduler);

        snapshots.OnNext([new FakeSession("Spotify")]);
        await Assert.That(cache.Count).IsEqualTo(1);

        snapshots.OnNext([]);
        scheduler.AdvanceBy(RemovalDelay.Ticks + TimeSpan.FromSeconds(1).Ticks);
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

    [Test]
    public async Task A_session_absent_for_less_than_the_window_is_never_removed()
    {
        var scheduler = new TestScheduler();
        var snapshots = new Subject<IReadOnlyList<FakeSession>>();
        var spotify = new FakeSession("Spotify");
        var reasons = new List<ChangeReason>();

        using var cache = SMTCSessionRepository
            .ToSessionChangeSet(snapshots, session => session.Aumid, RemovalDelay, scheduler)
            .Do(changeSet =>
            {
                foreach (var change in changeSet)
                {
                    reasons.Add(change.Reason);
                }
            })
            .AsObservableCache();

        // A track skip: gone for less than the window, then back before it elapses.
        snapshots.OnNext([spotify]);
        snapshots.OnNext([]);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        snapshots.OnNext([spotify]);
        scheduler.AdvanceBy(RemovalDelay.Ticks + TimeSpan.FromSeconds(1).Ticks);

        await Assert.That(cache.Keys).IsEquivalentTo(new[] { "Spotify" });
        await Assert.That(reasons).IsNotEmpty();
        await Assert.That(reasons).DoesNotContain(ChangeReason.Remove);
    }

    [Test]
    public async Task A_session_absent_for_longer_than_the_window_is_removed()
    {
        var scheduler = new TestScheduler();
        var snapshots = new Subject<IReadOnlyList<FakeSession>>();
        var spotify = new FakeSession("Spotify");

        using var cache = BuildCache(snapshots, scheduler);

        snapshots.OnNext([spotify]);
        await Assert.That(cache.Keys).IsEquivalentTo(new[] { "Spotify" });

        snapshots.OnNext([]);
        scheduler.AdvanceBy(RemovalDelay.Ticks + TimeSpan.FromSeconds(1).Ticks);

        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task An_add_is_immediate()
    {
        var scheduler = new TestScheduler();
        var snapshots = new Subject<IReadOnlyList<FakeSession>>();

        using var cache = BuildCache(snapshots, scheduler);

        snapshots.OnNext([new FakeSession("Spotify")]);

        await Assert.That(cache.Keys).IsEquivalentTo(new[] { "Spotify" });
    }

    [Test]
    public async Task An_update_to_an_existing_key_is_immediate()
    {
        var scheduler = new TestScheduler();
        var snapshots = new Subject<IReadOnlyList<FakeSession>>();

        using var cache = BuildCache(snapshots, scheduler);

        snapshots.OnNext([new FakeSession("Spotify", "First Track")]);
        snapshots.OnNext([new FakeSession("Spotify", "Second Track")]);

        await Assert.That(cache.Lookup("Spotify").Value.Title).IsEqualTo("Second Track");
    }
}
