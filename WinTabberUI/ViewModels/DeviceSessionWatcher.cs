using CoreAudio;
using CoreAudio.Interfaces;
using DynamicData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using WinTabberUI.Infrastructure;
using static CoreAudio.AudioSessionManager2;

namespace WinTabberUI.ViewModels;

public class DeviceSessionWatcher
{
    private readonly AudioSessionManager2 _manager;
    private SourceCache<AudioSession, string> _sessions;

    public DeviceSessionWatcher(AudioSessionManager2 manager)
    {
        _manager = manager;
    }

    public AudioSessionManager2 Manager => _manager;

    public IObservable<IChangeSet<AudioSession, string>> Connect()
    {
        return Observable.Create<IChangeSet<AudioSession, string>>(observer =>
        {

            _sessions = new SourceCache<AudioSession, string>(
                session => session.Aumid);
            var connection = _sessions.Connect();
            var disposables = new CompositeDisposable();


            var sourceSessions = _manager.Sessions!.AsEnumerable().OfType<AudioSessionControl2>().ToArray();
            _sessions.Edit(edits =>
            {

                foreach (var session in sourceSessions)
                {
                    var id = session.SessionIdentifier;
                    var instId = session.SessionInstanceIdentifier;
                    var name = session.DisplayName;
                    var process = Process.GetProcessById(Convert.ToInt32(session.ProcessID));
                    var aumid = process.TryGetAumid();
                    session.OnSessionDisconnected += Ac_OnSessionDisconnected;

                    Debug.WriteLine($"Session: {name}; {aumid};");
                    if (aumid is not null)
                    {
                        _sessions.AddOrUpdate(new AudioSession(aumid, name));
                    }
                }
            });

            var newSessions = Observable.FromEvent<SessionCreatedDelegate, IAudioSessionControl2>(handler =>
                {
                    SessionCreatedDelegate rawHandler = (sender, session) =>
                    {
                         handler(session);
                    };

                    return rawHandler;
                },
                handler => _manager.OnSessionCreated += handler,
                handler => _manager.OnSessionCreated -= handler);

            newSessions.Subscribe(session =>
            {

                session.GetSessionIdentifier(out var id);
                session.GetSessionInstanceIdentifier(out var instId);
                session.GetDisplayName(out var name);
                session.GetProcessId(out var pid);
                var p = Process.GetProcessById(Convert.ToInt32(pid));
                Debug.WriteLine($"NEW Session: {name}; {id}; {instId}; {p.MainModule.FileName}");

                if (session is AudioSessionControl2 ac)
                {
                    ac.OnSessionDisconnected += Ac_OnSessionDisconnected;
                }

                var aumid = session.TryGetAumid();
                if(aumid is not null)
                {
                    _sessions.AddOrUpdate(new AudioSession(aumid, name));
                }
            });




            connection.Subscribe(sessions => observer.OnNext(sessions))
              .DisposeWith(disposables);

            return () =>
            {
                disposables.Dispose();
            };
        }).Replay(1).RefCount();

    }

    private void Ac_OnSessionDisconnected(object sender, AudioSessionDisconnectReason disconnectReason)
    {
        if (sender is AudioSessionControl2 ac)
        {
            ac.OnSessionDisconnected -= Ac_OnSessionDisconnected;
            Debug.WriteLine($"Session ended {ac.DisplayName}");
        }
    }
}
