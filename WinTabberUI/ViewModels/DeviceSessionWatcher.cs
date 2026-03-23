//using DynamicData;
//using NAudio.CoreAudioApi;
//using NAudio.CoreAudioApi.Interfaces;
//using System;
//using System.Collections.Generic;
//using System.Collections.ObjectModel;
//using System.Diagnostics;
//using System.Linq;
//using System.Reactive.Disposables;
//using System.Reactive.Disposables.Fluent;
//using System.Reactive.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using System.Xml.Linq;
//using WinTabber.Api.Media.ShellApplications.Models;
//using WinTabberUI.Infrastructure;
//using WinTabberUI.Services;

//namespace WinTabberUI.ViewModels;

//public class DeviceSessionWatcher
//{
//    private readonly AudioSessionManager _manager;
//    private readonly IObservableCache<InstalledApplicationInfo, string> _installedApplicationsByPath;
//    private ReadOnlyObservableCollection<AudioSession> _sessions = new([]);

//    public DeviceSessionWatcher(AudioSessionManager manager, IObservableCache<InstalledApplicationInfo, string> installedApplicationsByPath)
//    {
//        _manager = manager;
//        _installedApplicationsByPath = installedApplicationsByPath;
//    }

//    public ReadOnlyObservableCollection<AudioSession> Sessions => _sessions;

//    public AudioSessionManager Manager => _manager;

//    public IObservable<IChangeSet<AudioSession, string>> Connect()
//    {
//        return Observable.Create<IChangeSet<AudioSession, string>>(observer =>
//        {

//            var resultSessions = new SourceCache<AudioSession, string>(session => session.AumId);
//            IObservable<IChangeSet<AudioSession, string>> connection = resultSessions.Connect();
//            var disposables = new CompositeDisposable();


//            var sourceSessions = GetNativeSessions().ToArray();
//            resultSessions.Edit(edits =>
//            {
//                CreateSessions(resultSessions, sourceSessions);
//            });

//            var newSessions = ObserveSessionCreation();

//            newSessions.Subscribe(nativeSession =>
//            {
//                var session = new AudioSessionControl(nativeSession);
//                CreateSession(resultSessions, session);
//            });

//            connection
//                .AutoRefresh(vm => vm.State)
//                .Filter(vm => vm.State == AudioSessionState.AudioSessionStateExpired)
//                .Bind(out _sessions)
//                .Subscribe();




//            connection.Subscribe(sessions => observer.OnNext(sessions))
//              .DisposeWith(disposables);

//            return () =>
//            {
//                disposables.Dispose();
//            };
//        }).Replay(1).RefCount();

//    }

//    private IObservable<IAudioSessionControl> ObserveSessionCreation()
//    {
//        return Observable.FromEvent<AudioSessionManager.SessionCreatedDelegate, IAudioSessionControl>(handler =>
//        {
//            AudioSessionManager.SessionCreatedDelegate rawHandler = (sender, session) =>
//            {
//                handler(session);
//            };

//            return rawHandler;
//        },
//        handler => _manager.OnSessionCreated += handler,
//        handler => _manager.OnSessionCreated -= handler);
//    }

//    private void CreateSessions(SourceCache<AudioSession, string> deviceSessions, AudioSessionControl[] sourceSessions)
//    {
//        foreach (var sourceSession in sourceSessions)
//        {
//            CreateSession(deviceSessions, sourceSession);
//        }
//    }

//    private void CreateSession(SourceCache<AudioSession, string> sessions, AudioSessionControl session)
//    {
//        if (!session.IsSystemSoundsSession && session.State != AudioSessionState.AudioSessionStateExpired)
//        {

//            var viewModel = AudioSession.Create(_installedApplicationsByPath, session);
//            if (viewModel is not null)
//            {
//                //Debug.WriteLine($"DeviceSessionWatcher - Session: {viewModel.DisplayName}; {viewModel.AumId};");
//                sessions.AddOrUpdate(viewModel);
//            }
//        }
//    }

//    private IEnumerable<AudioSessionControl> GetNativeSessions()
//    {
//        var count = _manager.Sessions.Count;
//        var sessions = _manager.Sessions;
//        for (int i = 0; i < count; i++)
//        {
//            var session = sessions[i];
//            if (session is not null)
//            {
//                yield return sessions[i];
//            }
//        }
//    }
//}
