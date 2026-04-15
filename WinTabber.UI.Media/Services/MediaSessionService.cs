using DynamicData;
using DynamicData.Kernel;
using System.Diagnostics.CodeAnalysis;
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
    CoreAudioDeviceRepository coreAudioDeviceRepository,
    AudioSessionService audioSessionService,
    SMTCSessionRepository mediaSessionRepository,
    InstalledApplicationRepository installedApplicationRepository
)
{
    // todo: implement updates
    

    private class NativeSessionWithApp(CoreAudioSessionWrapper Session, InstalledApplicationInfo? App)
    {
        public CoreAudioSessionWrapper Session { get; } = Session;
        public InstalledApplicationInfo? App { get; private set; } = App;

        public void UpdateApp(InstalledApplicationInfo? newApp)
        {
            App = newApp;
        }

        [MemberNotNullWhen(true, nameof(App))]
        public bool IsComplete => App != null;
    }

    record MediaSessionWithApp(GlobalSystemMediaTransportControlsSession Session, InstalledApplicationInfo App);

    private readonly CoreAudioDeviceRepository _coreAudioDeviceRepository = coreAudioDeviceRepository;
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
            //.AutoRefreshOnObservable(_ => appsByPath.Connect())
            .Transform(nativeSession =>
            {

                var processes = ProcessHelper.GetAncestors(nativeSession.ProcessId);
                foreach (var process in processes)
                {
                    if (process.TryGetExecutablePath(out var path))
                    {
                        var appOption = appsByPath.Lookup(path);
                        if (appOption.HasValue)
                        {
                            return new NativeSessionWithApp(nativeSession, appOption.Value);
                        }
                    }
                }

                return new NativeSessionWithApp(nativeSession, null);
            }, true)
            .Filter(item => item.IsComplete)
            .ChangeKey(session => session.App!.AppUserModelId)
            .AsObservableCache();
    }

    [Lazy]
    private IObservableCache<AggregateSession, string> GetMasterSessions()
    {
        var nativeSessionsWithApps = GetNativeSessionsWithApps();
        
        return GetSMTCSessionsByAumid()
            .ObserveOn(STAScheduler.Default) // ?
            .LeftJoin(
                nativeSessionsWithApps.Connect(),
                session => session.App!.AppUserModelId,
                (mediaSession, nativeSession) =>
                    new AggregateSession(
                        mediaSession.Session, 
                        mediaSession.App, 
                        nativeSession.ValueOrDefault()?.Session
                    )
            )
            .AutoRefreshOnObservable(_ => nativeSessionsWithApps.Connect())
            .AsObservableCache();
    }

    [Lazy]
    private IObservable<AggregateSession> GetActiveSession()
    {
        
        return _mediaSessionRepository.ActiveMediaSessionChanges
            .Select(smtcSession =>
            {
                if(smtcSession == null)
                {
                    return Observable.Empty<AggregateSession>();
                }
                return MasterSessions
                    .Watch(smtcSession.SourceAppUserModelId)
                    .Select(change => change.Current);
            })
            .Switch();
    }
}
