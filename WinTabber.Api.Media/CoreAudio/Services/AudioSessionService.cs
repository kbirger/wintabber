using DynamicData;
using DynamicData.Kernel;
using NAudio.CoreAudioApi.Interfaces;
using System.Reactive;
using System.Reactive.Linq;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Models;
using WinTabber.Api.Media.CoreAudio.Repositories;
using WinTabber.Api.Media.Repositories;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

namespace WinTabber.Api.Media.CoreAudio.Services;

public partial class AudioSessionService
{
    private readonly CoreAudioSessionRepository _sessionRepository;
    private readonly CoreAudioDeviceRepository _deviceRepository;

    public AudioSessionService(CoreAudioSessionRepository sessionRepository, CoreAudioDeviceRepository deviceRepository)
    { 
        _sessionRepository = sessionRepository;
        _deviceRepository = deviceRepository;


        var sessions = _deviceRepository
            .Devices
            .Connect()
            .MergeManyChangeSets(_sessionRepository.Connect)
            .DisposeMany()
            .FilterOnObservable(session => session.StateChanges.Take(1).Select(state => state == AudioSessionState.AudioSessionStateActive))
            .AsObservableCache();

        CoreAudioSessions = sessions;

        Sessions = sessions.Connect()
            .Transform(session => new SessionDto
            {
                SessionId = session.CoreAudioSession.GetSessionIdentifier,
                ProcessId = session.CoreAudioSession.GetProcessID,
                DisplayName = session.CoreAudioSession.DisplayName,
            })
            .AsObservableCache();


    }

    [Obsolete("Use the overload that accepts a SessionDto instead.")]
    public IObservable<Unit> SetVolume(CoreAudioSessionWrapper session, float volume)
    {
        return session.SetVolume(volume);
    }
    public IObservable<Unit> SetVolume(SessionDto session, float volume)
    {
        var nativeSession = CoreAudioSessions.Lookup(session.SessionId);

        if(nativeSession.HasValue)
        {
            return nativeSession.Value.SetVolume(volume);
        }
        return Observable.Return(Unit.Default);
    }


    public IObservableCache<CoreAudioSessionWrapper, string> CoreAudioSessions { get; } 

    public IObservableCache<SessionDto, string> Sessions { get; }
    private IObservable<IChangeSet<SessionDto, string>> GetSessions()
    {
        return _deviceRepository.Devices
            .Connect()
            .MergeManyChangeSets(_sessionRepository.Connect)
            .DisposeMany()
            .Transform(session => new SessionDto
            {
                SessionId = session.CoreAudioSession.GetSessionIdentifier,
                ProcessId = session.CoreAudioSession.GetProcessID,
                DisplayName = session.CoreAudioSession.DisplayName,
            })
            .Publish()
            .RefCount();
    }

    [Obsolete("Use the overload that accepts a SessionDto instead.")]
    public IObservable<Unit> SetMute(CoreAudioSessionWrapper session, bool isMuted)
    {
        return session.SetMute(isMuted);
    }

    public IObservable<Unit> SetMute(SessionDto session, bool mute)
    {
        var nativeSession = CoreAudioSessions.Lookup(session.SessionId);

        if (nativeSession.HasValue)
        {
            return nativeSession.Value.SetMute(mute);
        }
        return Observable.Return(Unit.Default);
    }


    //public IObservable<ObservableSessionDto> WatchSession(CoreAudioSessionWrapper session)
    //{
    //    return new ObservableSessionDto(session);

    //}
}
