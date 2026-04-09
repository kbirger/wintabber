using DynamicData;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using Windows.Media.Control;

namespace WinTabber.Api.Media.SMTC.Repositories;

public partial class SMTCSessionRepository
{

    [Lazy(IsPrivate = true)]
    private IObservable<GlobalSystemMediaTransportControlsSessionManager> GetSessionManagerObservable()
    {
        return Observable
            .StartAsync(async () => await GlobalSystemMediaTransportControlsSessionManager.RequestAsync())
            .Replay(1)
            .RefCount();
    }

    [Lazy(IsPrivate = true)]
    private IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> GetMediaSessionsChanges()
    {
        return GetSessionManagerObservable()
            .Select(GetSMTCSessionChanges)
            .Switch()
            .Replay(1)
            .RefCount();
    }

    [Lazy]
    private IObservable<GlobalSystemMediaTransportControlsSession?> GetActiveMediaSessionChanges()
    {
        return GetSessionManagerObservable()
            .Select(GetSMTCActiveSessionChanges)
            .Switch()
            .Replay(1)
            .RefCount();
    }

    [Lazy]
    private IObservable<IChangeSet<GlobalSystemMediaTransportControlsSession, string>> GetMediaSessions()
    {
        return MediaSessionsChanges
            .ToObservableChangeSet(s => s.SourceAppUserModelId);
    }

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
