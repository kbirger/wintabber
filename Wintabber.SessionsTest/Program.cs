

//using System.Diagnostics;
//using System.Reactive;
//using System.Reactive.Concurrency;
//using System.Reactive.Disposables;
//using System.Reactive.Disposables.Fluent;
//using System.Reactive.Linq;
//using DynamicData;
//using DynamicData.Kernel;
//using NAudio.CoreAudioApi;
//using ReactiveUI;
//using Windows.Media.Control;
//using WinTabber.Api.Media.CoreAudio.Repositories;
//using WinTabber.Api.Media.Repositories;
//using WinTabber.Api.Media.ShellApplications.Repositories;
//using WinTabber.Api.Media.SMTC.Repositories;
//using WinTabberUI.Infrastructure;
//using WinTabberUI.Models;
//using WinTabberUI.Services;
//using WinTabberUI.ViewModels;

//CoreAudioDeviceRepository deviceRepo = new CoreAudioDeviceRepository();
//CoreAudioSessionRepository sessionRepo = new CoreAudioSessionRepository();
//SMTCSessionRepository mediaSessionRepo = new SMTCSessionRepository();

//var ias = new InstalledApplicationRepository();

//var syncCtx = new SynchronizationContext();

//Debug.WriteLine($"Starting on thread {Environment.CurrentManagedThreadId}");

//SynchronizationContext.SetSynchronizationContext(syncCtx);
//Debug.WriteLine($"Continuing on thread {Environment.CurrentManagedThreadId}");

////var devices = deviceRepo.DevicesObservable.ToObservableChangeSet(d => d.ID).AsObservableCache();
//var devices = deviceRepo.Devices;

////Console.WriteLine($"{devices.Count} devices");
//devices.Subscribe(changes =>
//{
//    foreach (var item in changes)
//    {
//        Debug.WriteLine($"{item.Reason} {item.Current.FriendlyName}");
//    }
//});
//devices.QueryWhenChanged(x =>
//{
//    Debug.WriteLine($"?? {Environment.CurrentManagedThreadId}");
//    return x;
//}).Subscribe(cache =>
//{
//    //Console.Clear();
//    //Console.WriteLine("Devices:");
//    //foreach (var item in cache.Items)
//    //{
//    //    Console.WriteLine($"- {item.FriendlyName}");
//    //}
//});

//var nativeSessions = devices.MergeManyChangeSets(device =>
//{
//    var sessions = sessionRepo.Connect(device);

//    return sessions!;
//}).DisposeMany();


//var mediaSessions = mediaSessionRepo.MediaSessions;

//var mediaByAumid = mediaSessions
//    .AutoRefreshOnObservable(_ => ias.ApplicationsByAumid.Connect())
//    .InnerJoin(
//        ias.ApplicationsByAumid.Connect(),
//        app => app.AppUserModelId,
//        (session, app) =>
//        {
//            return new MediaSessionWithApp(session, app);
//        })
//    .ChangeKey(x => x.Session.SourceAppUserModelId)
//    .AsObservableCache();

//// installed apps get fetched multiple times!
//var nativeWithApp = nativeSessions.AutoRefreshOnObservable(_ => ias.ApplicationsByPath.Connect())
//    .Transform(nativeSession =>
//    {
//        var processes = ProcessHelper.GetAncestors(nativeSession.ProcessId);
//        var lookup = ias.ApplicationsByPath;
//        foreach (var process in processes)
//        {
//            if (process.TryGetExecutablePath(out var path))
//            {
//                var appOption = lookup.Lookup(path);
//                if (appOption.HasValue)
//                {
//                    return new NativeSessionWithApp(nativeSession, appOption.Value);
//                }
//            }
//        }

//        return new NativeSessionWithApp(nativeSession, null);
//    })
//    .Filter(item => item.App != null)
//    .AsObservableCache();


//var joined = mediaByAumid.Connect()
//    .LeftJoin(
//        nativeWithApp.Connect(),
//        session => session.App!.AppUserModelId,
//        (mediaSession, nativeSession) => new MasterSession(
//            mediaSession.Session,
//            mediaSession.App,
//            nativeSession.ValueOrDefault()?.Session
//        ))
//    .AutoRefreshOnObservable(_ => nativeWithApp.Connect());

//joined.Subscribe(x =>
//{
//    foreach (var item in x)
//    {
//        Debug.WriteLine($"{item.Reason} {item.Current.App.AppUserModelId}");
//    }
//});
//joined.QueryWhenChanged(x =>
//{
//    return x;
//}).Subscribe(z =>
//{
//    foreach (var item in z.Items)
//    {
//        Console.WriteLine($"Session {item.MediaSession.SourceAppUserModelId} - {item.App.Name} - {item.NativeSession?.State}");
//    }
//});


//record MasterSession(GlobalSystemMediaTransportControlsSession MediaSession, InstalledApplicationInfo App, CoreAudioSessionWrapper? NativeSession);
//record NativeSessionWithApp(CoreAudioSessionWrapper Session, InstalledApplicationInfo? App);
//record MediaSessionWithApp(GlobalSystemMediaTransportControlsSession Session, InstalledApplicationInfo App);

Console.WriteLine("disabled");