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
using WinTabber.Api.Media.ShellApplications.Models;
using WinTabber.Api.Media.ShellApplications.Repositories;
using WinTabber.Api.Media.SMTC.Repositories;
using WinTabber.Common.Util;
using WinTabber.Interop;
using WinTabber.UI.Media.Models;

namespace WinTabber.UI.Media.Services;
public partial class MediaSessionService(
    IAudioSessionService audioSessionService,
    SMTCSessionRepository mediaSessionRepository,
    IInstalledApplicationRepository installedApplicationRepository,
    [FromKeyedServices(STAScheduler.Key)] IScheduler staScheduler
) : IMediaSessionService
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

    private readonly IAudioSessionService _audioSessionService = audioSessionService;
    private readonly SMTCSessionRepository _mediaSessionRepository = mediaSessionRepository;
    private readonly IInstalledApplicationRepository _installedApplicationRepository = installedApplicationRepository;

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


    // NOT a LeftJoin: LeftJoin only computes the joined result once per left item and does not
    // re-run its result selector when the right side (nativeSessionsWithApps) later gains a key
    // that item needed -- confirmed live under the debugger, real Brave session, real native audio
    // session, both independently resolving to the exact same AppUserModelId, yet
    // AggregateSession.NativeSession stayed null indefinitely. This is a genuine race, not a
    // WinUI-3-specific defect: the native-session-to-app match (GetApp, walking process ancestry
    // against a Shell:AppsFolder enumeration that takes real time to complete) frequently loses the
    // race against the SMTC side, which is available almost immediately. It rarely shows up in the
    // long-running WPF app, where that enumeration is warm long before any specific session starts,
    // but can happen there too on a fresh launch with media already playing.
    //
    // AggregateSession.UpdateNativeSession/IsComplete/Key already exist specifically for this
    // (Key includes IsComplete precisely so a DistinctUntilChanged(session => session.Key) consumer
    // like GetActiveSession sees the None-to-Some transition as a real change) -- this was simply
    // never wired up. Fixed the same way GetNativeSessionsWithApps solves the identical problem one
    // level down (a real, already-working pattern in this file, not a new one): construct once with
    // whatever native match exists yet, then TransformWithInlineUpdate re-checks and calls
    // UpdateNativeSession every time nativeAppChanges emits, until a match is found.
    [Lazy]
    private IObservableCache<AggregateSession, string> GetMasterSessions()
    {
        var nativeSessionsWithApps = GetNativeSessionsWithApps();
        var nativeAppChanges = nativeSessionsWithApps.Connect();
        return GetSMTCSessionsByAumid()
            .ObserveOn(staScheduler)
            .AutoRefreshOnObservable(_ => nativeAppChanges)
            .TransformWithInlineUpdate(
                mediaSession =>
                    new AggregateSession(
                        mediaSession.Session,
                        mediaSession.App,
                        nativeSessionsWithApps.Lookup(mediaSession.App!.AppUserModelId).ValueOrDefault()?.Session
                    ),
                (aggregate, mediaSession) =>
                {
                    // No IsComplete short-circuit here, unlike GetNativeSessionsWithApps's own
                    // update action: that one matches against a shell-app catalog that only grows,
                    // so "already matched" really does mean "done forever." Native audio sessions
                    // churn constantly (confirmed live: CoreAudioSessionRepository.WatchForEnd fired
                    // AUDCLNT_E_DEVICE_INVALIDATED removals for several apps' sessions in one burst
                    // during ordinary startup) -- a once-matched NativeSession can go stale (a dead
                    // CoreAudioSessionWrapper pointing at an ended session) when the underlying
                    // native session ends and a new one starts. Re-assigning unconditionally on every
                    // refresh keeps this correct at the cost of a cheap cache lookup.
                    aggregate.UpdateNativeSession(
                        nativeSessionsWithApps.Lookup(mediaSession.App!.AppUserModelId).ValueOrDefault()?.Session
                    );
                },
                true)
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
