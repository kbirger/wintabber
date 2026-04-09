using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Text;
using DynamicData;
using DynamicData.Kernel;
using Windows.Media.Control;
using WinTabber.Api.Media.CoreAudio.Models;
using WinTabber.Api.Media.CoreAudio.Repositories;
using WinTabber.Api.Media.Repositories;
using WinTabber.Api.Media.ShellApplications.Models;
using WinTabber.Api.Media.ShellApplications.Repositories;
using WinTabber.Api.Media.SMTC.Repositories;
using WinTabberUI.Infrastructure;
using WinTabberUI.Models;

namespace WinTabberUI.Services;
public partial class MediaSessionService(
    CoreAudioDeviceRepository coreAudioDeviceRepository,
    CoreAudioSessionRepository coreAudioSessionRepository,
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
    private readonly CoreAudioSessionRepository _coreAudioSessionRepository = coreAudioSessionRepository;
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

    private IObservable<IChangeSet<CoreAudioSessionWrapper, string>> GetCoreAudioSessions()
    {
        return _coreAudioDeviceRepository
            .Devices
            .Connect()
            .MergeManyChangeSets(_coreAudioSessionRepository.Connect)
            .DisposeMany();
    }

    private IObservableCache<NativeSessionWithApp, string> GetNativeSessionsWithApps()
    {
        var appsByPath = _installedApplicationRepository.ApplicationsByPath;
        return GetCoreAudioSessions()
            .Filter( x =>
            {
                Debug.WriteLine($"Session: {x.DisplayName}");
                return true;
            })
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
