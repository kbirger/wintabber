using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using DynamicData;
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
    /// Every list from the session manager is a full snapshot of the sessions that exist now. A
    /// session that is absent from a later snapshot has ended, so it must produce a remove.
    /// </remarks>
    public static IObservable<IChangeSet<T, string>> ToSessionChangeSet<T>(
        IObservable<IReadOnlyList<T>> snapshots,
        Func<T, string> keySelector
    )
        where T : notnull
    {
        // EditDiff, not ToObservableChangeSet: ToObservableChangeSet treats each list as a batch of
        // adds and never removes, so ended sessions stayed in the media controls list forever.
        return snapshots.EditDiff(keySelector);
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
