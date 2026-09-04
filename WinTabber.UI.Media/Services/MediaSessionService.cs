using DynamicData;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Windows.Media.Control;
using WinTabber.Api.Media.CoreAudio.Models;
using WinTabber.Api.Media.CoreAudio.Repositories;
using WinTabber.Api.Media.CoreAudio.Services;
using WinTabber.Api.Media.Repositories;
using WinTabber.Api.Media.ShellApplications.Models;
using WinTabber.Api.Media.ShellApplications.Repositories;
using WinTabber.Api.Media.SMTC.Repositories;
using WinTabber.Common.Util;
using WinTabber.Interop;
using WinTabber.UI.Media.Models;

namespace WinTabber.UI.Media.Services;
public partial class MediaSessionService(
    AudioSessionService audioSessionService,
    SMTCSessionRepository mediaSessionRepository,
    InstalledApplicationRepository installedApplicationRepository,
    [FromKeyedServices(STAScheduler.Key)] IScheduler staScheduler
)
{
    // todo: implement updates
    

    private class NativeSessionWithApp(CoreAudioSessionWrapper session, InstalledApplicationInfo? app)
    {
        public CoreAudioSessionWrapper Session { get; } = session;
        public InstalledApplicationInfo? App { get; private set; } = app;


        public void UpdateApp(InstalledApplicationInfo? newApp)
        {
            App = newApp;
        }

        [MemberNotNullWhen(true, nameof(App))]
        public bool IsComplete => App != null;
    }

    record MediaSessionWithApp(GlobalSystemMediaTransportControlsSession Session, InstalledApplicationInfo App);

    private readonly AudioSessionService _audioSessionService = audioSessionService;
    private readonly SMTCSessionRepository _mediaSessionRepository = mediaSessionRepository;
    private readonly InstalledApplicationRepository _installedApplicationRepository = installedApplicationRepository;

    private IObservable<IChangeSet<MediaSessionWithApp, string>> GetSMTCSessionsByAumid()
    {
        var appsByAumid = _installedApplicationRepository.ApplicationsByAumid.Connect();
        return _mediaSessionRepository
            .MediaSessions.AutoRefreshOnObservable(_ => appsByAumid)
            .InnerJoin(
                appsByAumid,
                app => app.AppUserModelId,
                (session, app) =>
                {
                    return new MediaSessionWithApp(session, app);
                }
            )
            .ChangeKey(x => x.Session.SourceAppUserModelId);
    }

    

    private IObservableCache<NativeSessionWithApp, string> GetNativeSessionsWithApps()
    {
        var appsByPath = _installedApplicationRepository.ApplicationsByPath;
        return _audioSessionService.CoreAudioSessions
            .Connect()                        
            .AutoRefreshOnObservable(_ => appsByPath.Connect())
            .TransformWithInlineUpdate(
                nativeSession =>
                {
                    var app = GetApp(nativeSession.ProcessId, appsByPath);
                    return new NativeSessionWithApp(nativeSession, app);
                },
                (item, wrapper) => 
                {
                    if(item.IsComplete)
                    {
                        return;
                    }
                    var app = GetApp(wrapper.ProcessId, appsByPath);
                    if(app is not null)
                    {
                        item.UpdateApp(app);
                    }

                }, 
                true)
            .Filter(item => item.IsComplete)
            .ChangeKey(session => session.App!.AppUserModelId)
            .AsObservableCache();
    }

    private static InstalledApplicationInfo? GetApp(uint processId, IObservableCache<InstalledApplicationInfo, string> appsByPath)
    {
        var processes = ProcessHelper.GetAncestors(processId);
        foreach (var process in processes)
        {
            if (process.TryGetExecutablePath(out var path))
            {
                var appOption = appsByPath.Lookup(path);
                if (appOption.HasValue)
                {
                    return appOption.Value;
                }
            }
        }

        return null;
    }


    [Lazy]
    private IObservableCache<AggregateSession, string> GetMasterSessions()
    {
        var nativeSessionsWithApps = GetNativeSessionsWithApps();
        var nativeAppChanges = nativeSessionsWithApps.Connect();
        return GetSMTCSessionsByAumid()
            .ObserveOn(staScheduler)
            .LeftJoin(
                nativeAppChanges,
                session => session.App!.AppUserModelId,
                (mediaSession, nativeSession) =>
                    new AggregateSession(
                        mediaSession.Session, 
                        mediaSession.App, 
                        nativeSession.ValueOrDefault()?.Session
                    )
            )
            .AutoRefreshOnObservable(_ => nativeAppChanges)
            .AsObservableCache();
    }

    [Lazy]
    private IObservable<AggregateSession> GetActiveSession()
    {
        
        return _mediaSessionRepository.ActiveMediaSessionChanges
            .Select(smtcSession =>
            {
                if (smtcSession == null)
                    return Observable.Empty<AggregateSession>();
                return MasterSessions
                    .Watch(smtcSession.SourceAppUserModelId)
                    .Select(change => change.Current)
                    .DistinctUntilChanged(session => session.Key);
            })
            .Switch();
    }
}
