

using System.Diagnostics;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Kernel;
using NAudio.CoreAudioApi;
using ReactiveUI;
using WinTabberUI.Services;
using WinTabberUI.ViewModels;

var manager = new MediaSessionManager();
var ias = new InstalledApplicationService();
var adm = new AudioDeviceManager();
//var asm = new AudioSessionManager()

SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
var nativeSessions = adm.Connect()
    .TransformMany(device => new DeviceSessionWatcher(device.Device.AudioSessionManager, ias.ApplicationsByPath)
        .Connect()
        .AsObservableCache(),
        x => x.AumId)
    .AsObservableCache();


//manager.CurrentMediaSessions.Connect()..QueryWhenChanged().Subscribe(sessions => sessions.)
//manager.CurrentMediaSessions.Connect().ForEachItemChange(change =>
//{
//    var id = change.Current.Id;
//    switch (change.Reason)
//    {
//        case DynamicData.ListChangeReason.Add:
//            Console.WriteLine($"Session Added: {id}");
//            break;
//        case DynamicData.ListChangeReason.Remove:
//            Console.WriteLine($"Session Removed: {id}");
//            break;
//        default:
//            Console.WriteLine($"Session Changed: {id} - {change.Reason}");
//            break;
//    }

//    foreach (var s in manager.CurrentMediaSessions.Items)
//    {
//        var lookup = ias.ApplicationsByAumid.WatchValue(s.Id).Subscribe(lookup =>
//        {

//            //var found = "found" : "f;
//            var name = lookup.Name;
//            var tpath = lookup.TargetPath;
//            var ipath = lookup.PackageInstallPath;

//            Console.WriteLine($" - {s.Name} - {name} - {tpath} - {ipath}");
//        });
//    }
//}).Subscribe();

var sessionsWithApp = manager.MediaSessionsChangeSet    
    .InnerJoin(
        ias.ApplicationsByAumid.Connect(),
        app => app.AppUserModelId,
        (session, app) => new
        {
            Session = session,
            App = app
        });

sessionsWithApp.ForEachChange(s =>
{
    //foreach (var item in s)
    {
        Debug.WriteLine($"Media Session: {s.Current.Session.Id}");
        Debug.WriteLine($"  Media app: {s.Current.App.AppUserModelId} - {s.Current.App.TargetPath} - {s.Current.App.PackageInstallPath}");
        Debug.WriteLine($" ");


    }
}).Subscribe();


nativeSessions.Connect()
    .ChangeKey(x => x.ProcessFilePath!)
    .QueryWhenChanged(x => x)
    .Subscribe(x =>
    {
        var zz = ias.ApplicationsByPath;
        foreach (var item in x.Items)
        {
            Debug.WriteLine($"native session: {item.AumId} - {item.DisplayName} - {item.ProcessFilePath}");
        }
    });
var deviceSessionsWithApp = ias.ApplicationsByPath.Connect()
    .InnerJoin(
        nativeSessions.Connect(),
        session => session.ProcessFilePath!,

        (app, session) => new
        {
            Session = session,
            App = app
        });

deviceSessionsWithApp.QueryWhenChanged(z => z)
        .Subscribe(s =>
        {
            foreach (var item in s.Items)
            {
                Debug.WriteLine($"Device Session: {item.Session.AumId}");
                Debug.WriteLine($"  Device app: {item.App.AppUserModelId} - {item.App.TargetPath} - {item.App.PackageInstallPath}");
                Debug.WriteLine($" ");


            }
        });

//deviceSessionsWithApp.ForEachChange(s =>
//{
//    //foreach (var item in s)
//    {
//        Debug.WriteLine($"Device Session: {s.Current.Session.AumId}");
//        Debug.WriteLine($"  Device app: {s.Current.App.AppUserModelId} - {s.Current.App.TargetPath} - {s.Current.App.PackageInstallPath}");
//        Debug.WriteLine($" ");


//    }
//}).Subscribe();

var x = sessionsWithApp
    .ChangeKey(item => item.Session.Id)
    .InnerJoin(
        deviceSessionsWithApp,
        session => session.App.AppUserModelId,
        (media, device) => new
        {
            MediaSession = media.Session,
            MediaApp = media.App,
            DeviceSession = device.Session,
            DeviceApp = device.App
        }
    ).QueryWhenChanged(x => x)
    .Subscribe(s =>
    {
        foreach (var item in s.Items)
        {
            Debug.WriteLine($"Joined Session: {item.MediaSession.Id}");
            //Debug.WriteLine($"  Device app: {item.App.AppUserModelId} - {item.App.TargetPath} - {item.App.PackageInstallPath}");
            Debug.WriteLine($" ");


        }
    });

//x.Connect().ForEachChange(_ =>
//{

//    foreach (var item in x.Items)
//    {
//        Debug.WriteLine($"Session: {item.MediaSession.Id}");
//        Debug.WriteLine($"  Media app: {item.MediaApp.AppUserModelId} - {item.MediaApp.TargetPath}");
//        Debug.WriteLine($"Device Session: {item.DeviceSession.AumId} - {item.DeviceSession.DisplayName}");
//        Debug.WriteLine($"  Media app: {item.DeviceApp.AppUserModelId} - {item.DeviceApp.TargetPath}");
//        Debug.WriteLine($" ");


//    }
//}).Subscribe();
//manager.CurrentMediaSessions.Connect().CombineLatest(
//    adm.Connect().WhereReasonsAreNot(ChangeReason.Remove),
//    ias.ApplicationsByAumid.Connect().WhereReasonsAreNot(ChangeReason.Remove),
//    ias.ApplicationsByPath.Connect().WhereReasonsAreNot(ChangeReason.Remove),
//    (mediaSessions, audioDeviceSessions, aumidLookup, pathLookup) =>
//{
//    return from mediaSession in mediaSessions
//           from audioDeviceSession in audioDeviceSessions
//           where aumidLookup
//});
await Task.Delay(-1);
//// See https://aka.ms/new-console-template for more information
////using CoreAudio;
//using NAudio;
//using NAudio.CoreAudioApi;
//using NAudio.CoreAudioApi.Interfaces;
//using NAudio.Wasapi;
//using System.Diagnostics;
//using WinTabberUI.ViewModels;

//Debug.WriteLine("Hello, World!");

//MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
//var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
//List<Monitor> monitors = new List<Monitor>();
////var monitor = new Monitor();
//List<AudioSessionControl> _sessions = new();
//foreach (var endpoint in endpoints)
//{
//    endpoint.AudioSessionManager.OnSessionCreated += AudioSessionManager_OnSessionCreated;

//    for(int i = 0; i < endpoint.AudioSessionManager.Sessions.Count; i++)
//    {
//        var session = endpoint.AudioSessionManager.Sessions[i];
//        if(session.IsSystemSoundsSession)
//        {
//            continue;
//        }
//        _sessions.Add(session);
//        var monitor = new Monitor(session, _sessions);
//        session.RegisterEventClient(monitor);
//        var name = session.DisplayName;
//        var pid = session.GetProcessID;
//        var proc = Process.GetProcessById(Convert.ToInt32(pid));
//        Debug.WriteLine($"existing session '{name}': {proc.MainModule?.FileName}"); 

//        if(i == endpoint.AudioSessionManager.Sessions.Count - 1)
//        {
//            monitor.Print();
//        }
//    }
//}

//void AudioSessionManager_OnSessionCreated(object sender, NAudio.CoreAudioApi.Interfaces.IAudioSessionControl newSession)
//{
//    //newSession.GetDisplayName(out var name);
//    var session = new AudioSessionControl(newSession);
//    _sessions.Add(session);
//    var name = session.DisplayName;
//    var pid = session.GetProcessID;
//    var proc = Process.GetProcessById(Convert.ToInt32(pid));
//    Debug.WriteLine($"new session '{name}': {proc.MainModule?.FileName}");
//    //newSession.RegisterAudioSessionNotification(new Monitor());
//    var monitor = new Monitor(session, _sessions);
//    newSession.RegisterAudioSessionNotification(new AudioSessionEventsCallback(monitor));
//    monitor.Print();
//}

////var watchers = endpoints.Select(x => new DeviceSessionWatcher());
//while(true)
//{
//    await Task.Delay(100);

//}

//public record Session(string name, string process)
//{

//}

//public class Monitor : IAudioSessionEventsHandler
//{
//    private AudioSessionControl _session;
//    private readonly List<AudioSessionControl> _sessions;

//    public Monitor(AudioSessionControl session, List<AudioSessionControl> sessions)
//    {
//        _session = session;
//        _sessions = sessions;
//    }

//    public void OnVolumeChanged(float volume, bool isMuted)
//    {
//        //throw new NotImplementedException();
//    }

//    public void OnDisplayNameChanged(string displayName)
//    {
//        Debug.WriteLine($"New display name {displayName}");
//    }

//    public void OnIconPathChanged(string iconPath)
//    {
//        //throw new NotImplementedException();
//    }

//    public void OnChannelVolumeChanged(uint channelCount, nint newVolumes, uint channelIndex)
//    {
//        //throw new NotImplementedException();
//    }

//    public void OnGroupingParamChanged(ref Guid groupingId)
//    {
//        Debug.WriteLine($"New grouping {groupingId}");
//    }

//    public void OnStateChanged(AudioSessionState state)
//    {
//        Debug.WriteLine($"New session state {state}");
//        if(state == AudioSessionState.AudioSessionStateInactive || state == AudioSessionState.AudioSessionStateExpired)
//        {
//            _sessions.Remove(_session);
//        }
//        Print();
//    }

//    public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
//    {
//        Debug.WriteLine($"session2 disconnected");
//    }

//    public void Print()
//    {
//        Debug.WriteLine("Sessions:");
//        foreach(var session in _sessions)
//        {
//            PrintSession(session);
//        }
//        Debug.WriteLine("");
//    }

//    private void PrintSession(AudioSessionControl session)
//    {
//        Debug.WriteLine($"- {session.State} Session {session.DisplayName}: {GetFilePath(session.GetProcessID)}");
//    }

//    public string GetFilePath(uint pid)
//    {
//        return Process.GetProcessById(Convert.ToInt32(pid)).MainModule.FileName;
//    }
//}

////foreach(var watcher in watchers)
////{
////    watcher.Connect().Subscribe(changeSet =>
////    {
////        foreach(var change in changeSet)
////        {
////            switch(change.Reason)
////            {
////                case DynamicData.ChangeReason.Add:
////                    Debug.WriteLine($"Session Added: {change.Key}");
////                    break;
////                case DynamicData.ChangeReason.Remove:
////                    Debug.WriteLine($"Session Removed: {change.Key}");
////                    break;
////                case DynamicData.ChangeReason.Update:
////                    Debug.WriteLine($"Session Updated: {change.Key}");
////                    break;
////            }
////        }
////    });
////}
////DeviceSessionWatcher watcher = );

