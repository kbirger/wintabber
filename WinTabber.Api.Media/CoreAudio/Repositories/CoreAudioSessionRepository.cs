using DynamicData;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using WinTabber.Api.Media.CoreAudio.Models;
using WinTabber.Common.Util;

namespace WinTabber.Api.Media.CoreAudio.Repositories;

public class CoreAudioSessionRepository : IDisposable
{
    public IObservable<IChangeSet<CoreAudioSessionWrapper, string>> Connect(CoreAudioDeviceWrapper device)
    {
        return Observable
            .Defer(() => GetDeviceSessions(device))
            .SubscribeOn(Scheduler) // ?
            .ObserveOn(Scheduler);
    }

    private IObservable<IChangeSet<CoreAudioSessionWrapper, string>> GetDeviceSessions(CoreAudioDeviceWrapper device)
    {
        var manager = device.Device.AudioSessionManager;
        var changes = ObservableChangeSet.Create<CoreAudioSessionWrapper, string>(
                (cache) =>
                {
                    var scheduler = Scheduler;

                    // ToList matters. The projection is lazy, so a second enumeration would build a
                    // second set of wrappers that the cache does not hold.
                    var initialSessions = GetNativeSessions(manager)
                        .Select(session => new CoreAudioSessionWrapper(session, device, scheduler))
                        .ToList();

                    cache.AddOrUpdate(initialSessions);

                    // Every end-of-session subscription lives here, so that the caller releases them
                    // all when it disposes the change set.
                    var endSubscriptions = new CompositeDisposable();

                    foreach (var session in initialSessions)
                    {
                        WatchForEnd(session, cache, endSubscriptions);
                    }

                    var newSessions = ObserveSessionCreation(manager);

                    var subscription = newSessions
                        .SubscribeOn(Scheduler)
                        .ObserveOn(Scheduler)
                        .Subscribe(nativeSession =>
                        {
                            var session = new AudioSessionControl(nativeSession);
                            if (session.IsSystemSoundsSession)
                            {
                                return;
                            }
                            var wrapper = new CoreAudioSessionWrapper(session, device, scheduler);
                            WatchForEnd(wrapper, cache, endSubscriptions);
                            cache.AddOrUpdate(wrapper);
                        })
                    ;

                    return new CompositeDisposable(endSubscriptions, subscription);
                },
                item => item.CoreAudioSession.GetSessionInstanceIdentifier
            )
            .SubscribeOn(Scheduler)
            .ObserveOn(Scheduler)
            .AutoRefreshOnObservable(session => session.SessionChanged
                .Log(x => "Session changed triggering refresh")
                );

        return changes;
    }

    public IScheduler Scheduler => _scheduler;

    private readonly IScheduler _scheduler;

    public CoreAudioSessionRepository(IScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    /// <summary>
    /// Removes the session from the cache when the session ends.
    /// </summary>
    private static void WatchForEnd(
        CoreAudioSessionWrapper session,
        ISourceCache<CoreAudioSessionWrapper, string> cache,
        CompositeDisposable subscriptions
    )
    {
        subscriptions.Add(session.SessionEnded.Take(1).Subscribe(_ => cache.Remove(session)));
    }

    private static IEnumerable<AudioSessionControl> GetNativeSessions(AudioSessionManager manager)
    {
        var count = manager.Sessions.Count;
        var sessions = manager.Sessions;
        for (int i = 0; i < count; i++)
        {
            var session = sessions[i];
            if (session is not null && !session.IsSystemSoundsSession)
            {
                yield return session;
            }
        }
    }
    private static IObservable<IAudioSessionControl> ObserveSessionCreation(AudioSessionManager manager)
    {
        return Observable.FromEvent<AudioSessionManager.SessionCreatedDelegate, IAudioSessionControl>(
            handler =>
            {
                AudioSessionManager.SessionCreatedDelegate rawHandler = (sender, session) =>
                {
                    handler(session);
                };

                return rawHandler;
            },
            handler => manager.OnSessionCreated += handler,
            handler => manager.OnSessionCreated -= handler
        );
    }

    /// <summary>
    /// Does nothing, because the repository owns nothing.
    /// </summary>
    /// <remarks>
    /// Every subscription that Connect creates belongs to its subscriber, and the scheduler belongs
    /// to the container. The type stays disposable because the container registers it as a
    /// singleton and calls Dispose at shutdown.
    /// </remarks>
    public void Dispose() { }
}
