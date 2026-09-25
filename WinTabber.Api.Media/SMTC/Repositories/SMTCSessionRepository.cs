using System.Diagnostics;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;
using Windows.Media.Control;

namespace WinTabber.Api.Media.SMTC.Repositories;

public partial class SMTCSessionRepository(ISmtcSessionSource sessionSource)
{
    [Lazy(IsPrivate = true)]
    private IObservable<GlobalSystemMediaTransportControlsSessionManager> GetSessionManagerObservable()
    {
        return Observable
            .StartAsync(async () => await sessionSource.RequestAsync())
            .Replay(1)
            .RefCount();
    }

    [Lazy(IsPrivate = true)]
    private IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> GetMediaSessionsChanges()
    {
        return GetSessionManagerObservable().Select(GetSMTCSessionChanges).Switch().Replay(1).RefCount();
    }

    [Lazy]
    private IObservable<GlobalSystemMediaTransportControlsSession?> GetActiveMediaSessionChanges()
    {
        return GetSessionManagerObservable()
            .Select(GetSMTCActiveSessionChanges)
            .Switch()
            .DistinctUntilChanged(session => session?.SourceAppUserModelId)
            .Replay(1)
            .RefCount();
    }

    [Lazy]
    private IObservable<IChangeSet<GlobalSystemMediaTransportControlsSession, string>> GetMediaSessions()
    {
        return ToSessionChangeSet(MediaSessionsChanges, session => session.SourceAppUserModelId);
    }

    /// <summary>
    /// Turns a stream of SMTC session snapshots into a change set.
    /// </summary>
    /// <remarks>
    /// Every list from the session manager is a full snapshot of the sessions that exist now. An
    /// add or an update applies at once; a session absent from a later snapshot only produces a
    /// remove once it has stayed absent for <paramref name="removalDelay"/>, so a session that
    /// briefly disappears and returns (a track skip) never produces one at all.
    /// </remarks>
    public static IObservable<IChangeSet<T, string>> ToSessionChangeSet<T>(
        IObservable<IReadOnlyList<T>> snapshots,
        Func<T, string> keySelector,
        TimeSpan? removalDelay = null,
        IScheduler? scheduler = null
    )
        where T : notnull
    {
        // 1.5s covers the worst measured gap on a track skip (950ms, see
        // IDEAS-SESSION-SELECTION.md) with headroom. The cost is that a session which really
        // ended lingers in the list up to 1.5s longer than before - already several seconds in
        // practice, since SMTC itself is slow to report a closed app as gone.
        var delay = removalDelay ?? TimeSpan.FromSeconds(1.5);
        var delayScheduler = scheduler ?? Scheduler.Default;

        // EditDiff is no longer used here: it applies a removal the instant a session is
        // missing from one snapshot, and SMTC removes-then-re-adds a session on every track
        // skip. An add is positive evidence (the session exists, here it is); a removal is an
        // inference from absence in a single snapshot. Adds and updates apply at once below; a
        // removal is instead a proposal that must survive `delay` before it reaches the cache,
        // so a session that returns within the window never produces a removal at all.

        // Step 1: per-key presence events. Scan carries the set of keys known from the previous
        // snapshot so absence can be computed - a key missing from the new snapshot is a
        // Present:false event; every key present in the new snapshot is a Present:true event.
        var presenceEvents = snapshots
            .Scan(
                (Known: new HashSet<string>(), Events: (IReadOnlyList<PresenceEvent<T>>)[]),
                (state, snapshot) =>
                {
                    var currentKeys = new HashSet<string>(snapshot.Select(keySelector));
                    var events = new List<PresenceEvent<T>>(snapshot.Count);
                    foreach (var item in snapshot)
                    {
                        events.Add(new PresenceEvent<T>(keySelector(item), item, true));
                    }
                    foreach (var goneKey in state.Known)
                    {
                        if (!currentKeys.Contains(goneKey))
                        {
                            events.Add(new PresenceEvent<T>(goneKey, default, false));
                        }
                    }
                    return (currentKeys, (IReadOnlyList<PresenceEvent<T>>)events);
                }
            )
            .SelectMany(state => state.Events);

        // Step 2: debounce absence per key. GroupBy splits the presence events by key; within
        // each key's group, a Present event is believed at once (Observable.Return, on no
        // scheduler - the add/update tests assert with zero time advance) and an absent event
        // is only a proposal, delayed on `delayScheduler`. Switch is the whole trick: a later
        // Present event unsubscribes whichever delayed Absent is still pending, so a key that
        // returns inside the window never emits a removal at all. No per-key timer dictionary,
        // no manual cancellation bookkeeping.
        //
        // DistinctUntilChanged here uses full event equality, not just the Present flag: a
        // steady, unchanged key would otherwise re-emit (and re-issue AddOrUpdate) on every
        // snapshot even when nothing changed, but comparing only Present would also swallow a
        // genuine value change for a key that stays present - which AddOrUpdate must still see,
        // to preserve the update semantics EditDiff gave.
        var debounced = presenceEvents
            .GroupBy(e => e.Key)
            .SelectMany(group =>
                group
                    .Select(e => e.Present ? Observable.Return(e) : Observable.Return(e).Delay(delay, delayScheduler))
                    .Switch()
                    .DistinctUntilChanged()
            );

        // Step 3: drive a cache from the debounced presence stream. The cache is connected to
        // the observer before the feed is subscribed, so no changeset from the feed can be
        // missed; the feed, the connection and the cache are disposed together.
        return Observable.Create<IChangeSet<T, string>>(observer =>
        {
            var cache = new SourceCache<T, string>(keySelector);
            var connection = cache.Connect().Subscribe(observer);
            var feed = debounced.Subscribe(
                e =>
                {
                    if (e.Present)
                    {
                        cache.AddOrUpdate(e.Item!);
                    }
                    else
                    {
                        cache.RemoveKey(e.Key);
                    }
                },
                observer.OnError,
                observer.OnCompleted
            );

            return new CompositeDisposable(feed, connection, cache);
        });
    }

    private readonly record struct PresenceEvent<T>(string Key, T? Item, bool Present);

    private static IObservable<GlobalSystemMediaTransportControlsSession> GetSMTCActiveSessionChanges(
        GlobalSystemMediaTransportControlsSessionManager manager
    )
    {
        return Observable
            .FromEventPattern<CurrentSessionChangedEventArgs>(manager, nameof(manager.CurrentSessionChanged))
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default)
            .Select(_ => manager.GetCurrentSession());
    }

    private static IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> GetSMTCSessionChanges(
        GlobalSystemMediaTransportControlsSessionManager manager
    )
    {
        return Observable
            .FromEventPattern<SessionsChangedEventArgs>(manager, nameof(manager.SessionsChanged))
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default)
            .Select(_ => manager.GetSessions())
            .Do(sessions =>
            {
                Debug.WriteLine(string.Join(", ", sessions.Select(session => session.SourceAppUserModelId).ToArray()));
            });
    }
}
