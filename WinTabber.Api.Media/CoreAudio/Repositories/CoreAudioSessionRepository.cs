using DynamicData;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTabber.Api.Media.CoreAudio.Models;

namespace WinTabber.Api.Media.CoreAudio.Repositories;

public class CoreAudioSessionRepository : IDisposable
{
    public IObservable<IChangeSet<CoreAudioSessionWrapper, string>> Connect(MMDevice device)
    {
        var manager = device.AudioSessionManager;
        var changes = ObservableChangeSet.Create<CoreAudioSessionWrapper, string>((cache) =>
        {
            var initialSessions = GetNativeSessions(manager).Select(session => new CoreAudioSessionWrapper(session));
            cache.AddOrUpdate(initialSessions);

            initialSessions
                .Select(session =>
                    session.SessionEnded
                    .Take(1)
                    .Subscribe(_ =>
                    {
                        cache.Remove(session);
                    }
                )
            );

            var newSessions = ObserveSessionCreation(manager);

            var subscription = newSessions.Subscribe(nativeSession =>
            {
                var session = new AudioSessionControl(nativeSession);
                var wrapper = new CoreAudioSessionWrapper(session);
                wrapper.SessionEnded
                    .Take(1)
                    .Subscribe(_ =>
                    {
                        cache.Remove(wrapper);
                    });
                cache.AddOrUpdate(wrapper);
            });



            return new CompositeDisposable(subscription);
        },

        item => item.NativeSession.GetSessionInstanceIdentifier)
            .AutoRefreshOnObservable(session => session.SessionChanged);
            
        return changes;

    }

    private static IEnumerable<AudioSessionControl> GetNativeSessions(AudioSessionManager manager)
    {
        var count = manager.Sessions.Count;
        var sessions = manager.Sessions;
        for (int i = 0; i < count; i++)
        {
            var session = sessions[i];
            if (session is not null)
            {
                yield return sessions[i];
            }
        }
    }

    private static IObservable<IAudioSessionControl> ObserveSessionCreation(AudioSessionManager manager)
    {
        return Observable.FromEvent<AudioSessionManager.SessionCreatedDelegate, IAudioSessionControl>(handler =>
        {
            AudioSessionManager.SessionCreatedDelegate rawHandler = (sender, session) =>
            {
                handler(session);
            };

            return rawHandler;
        },
        handler => manager.OnSessionCreated += handler,
        handler => manager.OnSessionCreated -= handler);
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}
