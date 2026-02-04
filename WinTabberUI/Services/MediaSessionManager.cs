using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using Windows.Media.Control;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Services;

public partial class MediaSessionManager : ReactiveObject, IDisposable
{
    private CompositeDisposable _cleanup;

    public MediaSessionManager()
    {
        var currentSessionsSubscription = MediaSessionsChangeSet
            .BindToObservableList(out _currentSessions)
            .Subscribe();

        _activeSession = ActiveSessionChanges.ToProperty(this, m => m.ActiveSession);

        _cleanup = new CompositeDisposable(currentSessionsSubscription);
    }
    public void Dispose()
    {
        _cleanup?.Dispose();
    }

    IObservableList<MediaSession> _currentSessions;

    public IObservableList<MediaSession> CurrentMediaSessions => _currentSessions;
    private ObservableAsPropertyHelper<MediaSession?> _activeSession;

    public MediaSession? ActiveSession => _activeSession.Value;


    [Lazy(IsPrivate = true)]
    private IObservable<MediaSession?> GetActiveSessionChanges()
    {
        return GetNativeActiveSessionChanges()
            .Switch()
            .Do(x =>
            {
                Debug.WriteLine($"Session changed event: {x?.SourceAppUserModelId}");
            })
            .DistinctUntilChanged(x => x?.SourceAppUserModelId)
            .Select(CreateMediaSession)
            .Do(x =>
            {
                Debug.WriteLine($"WINRT: Current session changed. Got new session {x?.Id ?? "no session"}");
            })
            .Replay(1)
            .RefCount();
    }

    [Lazy(IsPrivate = false)]
    private IObservable<IChangeSet<MediaSession, string>> GetMediaSessionsChangeSet()
    {
        return ObservableChangeSet.Create<MediaSession, string>(cache =>
        {
            var subscription = MediaSessionsChanges
                .Subscribe(sessions =>
                {
                    var sessionItems = sessions
                        .Select(session => CreateMediaSession(session))
                        .ToArray();

                    cache.EditDiff(sessionItems, (oldSession, newSession) => oldSession.Id == newSession.Id);
                });
            return subscription;
        }, session => session.Id);
    }

    
    [return: NotNullIfNotNull(nameof(session))]
    private static MediaSession? CreateMediaSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session is null)
        {
            return null;
        }

        return MediaSession.Create(session, new Infrastructure.InstalledApplicationInfo() { AppUserModelId = "", Icon = Observable.Empty<ImageSource>(), Name = "" });
    }

    [Lazy(IsPrivate = true)]
    private IObservable<GlobalSystemMediaTransportControlsSessionManager> GetManager()
    {
        return Observable.FromAsync(async () => await GlobalSystemMediaTransportControlsSessionManager.RequestAsync())
            .Replay(1)
            .RefCount()
            .ObserveOn(RxApp.MainThreadScheduler);
    }
    [Lazy(IsPrivate = true)]
    private IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> GetMediaSessionsChanges()
    {
        return GetNativeMediaSessionsChanges()
        .Switch()
        .Replay(1)
        .RefCount();
    }

    private IObservable<IObservable<GlobalSystemMediaTransportControlsSession>> GetNativeActiveSessionChanges()
    {
        return Manager.Select(manager =>
            EventHelper.EventOrEmpty<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs, GlobalSystemMediaTransportControlsSession>(
                manager,
                h => manager.CurrentSessionChanged += h,
                h => manager.CurrentSessionChanged -= h,
                events => events.Select(_ => manager.GetCurrentSession())
                ));
    }

    private IObservable<IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>>> GetNativeMediaSessionsChanges()
    {
        return Manager.Select(manager =>
            EventHelper.EventOrEmpty<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs, IReadOnlyList<GlobalSystemMediaTransportControlsSession>>(
                manager,
                h => manager.SessionsChanged += h,
                h => manager.SessionsChanged -= h,
                events => events.Select(_ => manager.GetSessions())
            )
            .Do(sessions => { Debug.WriteLine("SessionsChanged: {0}", string.Join(", ", sessions.Select(session => session.SourceAppUserModelId).ToArray())); })
        );
    }
}
