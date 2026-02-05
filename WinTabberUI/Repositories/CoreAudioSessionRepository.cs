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
using WinTabberUI.Models;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Repositories;

public class CoreAudioSessionRepository : IDisposable
{
    public IObservable<IChangeSet<CoreAudioSessionWrapper, string>> Connect(MMDevice device)
    {
        var manager = device.AudioSessionManager;
        var changes = ObservableChangeSet.Create<CoreAudioSessionWrapper, string>((cache) =>
        {
            cache.AddOrUpdate(GetNativeSessions(manager).Select(session => new CoreAudioSessionWrapper(session)));

            var newSessions = ObserveSessionCreation(manager);

            var subscription = newSessions.Subscribe(nativeSession =>
            {
                var session = new AudioSessionControl(nativeSession);
                var wrapper = new CoreAudioSessionWrapper(session);
                cache.AddOrUpdate(wrapper);
            });



            return new CompositeDisposable(subscription);
        },

        item => item.SessionInstanceIdentifier)
        .AutoRefresh(vm => vm.State);
            
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
