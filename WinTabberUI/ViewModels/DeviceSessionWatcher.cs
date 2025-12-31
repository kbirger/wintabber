using DynamicData;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using WinTabberUI.Infrastructure;

namespace WinTabberUI.ViewModels;

public class DeviceSessionWatcher
{
    private readonly AudioSessionManager _manager;
    private ReadOnlyObservableCollection<AudioSession> _sessions = new([]);

    public DeviceSessionWatcher(AudioSessionManager manager)
    {
        _manager = manager;
    }

    public ReadOnlyObservableCollection<AudioSession> Sessions => _sessions;

    public AudioSessionManager Manager => _manager;

    public IObservable<IChangeSet<AudioSession, string>> Connect()
    {
        return Observable.Create<IChangeSet<AudioSession, string>>(observer =>
        {

            var sessions = new SourceCache<AudioSession, string>(session => session.AumId);
            var connection = sessions.Connect();
            var disposables = new CompositeDisposable();


            var sourceSessions = GetSessions().ToArray();
            sessions.Edit(edits =>
            {

                foreach (var session in sourceSessions)
                {
                    var id = session.GetSessionIdentifier;
                    var instId = session.GetSessionInstanceIdentifier;
                    var viewModel = new AudioSession(session, (_) => { });
                    Debug.WriteLine($"Session: {viewModel.DisplayName}; {viewModel.AumId};");
                    if (viewModel.AumId is not null)
                    {
                        sessions.AddOrUpdate(viewModel);
                    }
                }
            });

            var newSessions = Observable.FromEvent<AudioSessionManager.SessionCreatedDelegate, IAudioSessionControl>(handler =>
                {
                    AudioSessionManager.SessionCreatedDelegate rawHandler = (sender, session) =>
                    {
                        handler(session);
                    };

                    return rawHandler;
                },
                handler => _manager.OnSessionCreated += handler,
                handler => _manager.OnSessionCreated -= handler);

            newSessions.Subscribe(session =>
            {

                var wrapper = new AudioSessionControl(session);
                var viewModel = new AudioSession(wrapper, (_) => { });
                Debug.WriteLine($"NEW Session: {viewModel.DisplayName}; {viewModel.AumId}; {viewModel.ProcessFilePath}");
                if (viewModel.State != AudioSessionState.AudioSessionStateExpired)
                {
                    sessions.AddOrUpdate(viewModel);
                }
            });

            connection
                .AutoRefresh(vm => vm.State)
                .Filter(vm => vm.State == AudioSessionState.AudioSessionStateExpired)
                .Bind(out _sessions)
                .Subscribe();




            connection.Subscribe(sessions => observer.OnNext(sessions))
              .DisposeWith(disposables);

            return () =>
            {
                disposables.Dispose();
            };
        }).Replay(1).RefCount();

    }

    private IEnumerable<AudioSessionControl> GetSessions()
    {
        var count = _manager.Sessions.Count;
        var sessions = _manager.Sessions;
        for (int i = 0; i < count; i++)
        {
            var session = sessions[i];
            if (session is not null)
            {
                yield return sessions[i];
            }
        }
    }
}
