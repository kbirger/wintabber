using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace WinTabberUI.Repositories;

public partial class MediaSessionRepository
{
    [Lazy(IsPrivate = true)]
    private IObservable<GlobalSystemMediaTransportControlsSessionManager> GetSessionManagerObservable()
    {
        return Observable.FromAsync(async () => 
            await GlobalSystemMediaTransportControlsSessionManager.RequestAsync())
                .Replay(1)
                .RefCount();
    }

    [Lazy]
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
            .Select(GetSMTCActiveSesionChanges)
            .Switch()
            .Replay(1)
            .RefCount();
    }

    private static IObservable<GlobalSystemMediaTransportControlsSession> GetSMTCActiveSesionChanges(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        return Observable.FromEventPattern<CurrentSessionChangedEventArgs>(manager, nameof(manager.CurrentSessionChanged))
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default)
            .Select(_ => manager.GetCurrentSession());
    }
    private static IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> GetSMTCSessionChanges(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        return Observable.FromEventPattern<SessionsChangedEventArgs>(manager, nameof(manager.SessionsChanged))
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default)
            .Select(_ => manager.GetSessions())
            .Do(sessions => { Debug.WriteLine(string.Join(", ", sessions.Select(session => session.SourceAppUserModelId).ToArray())); });
    }

}
