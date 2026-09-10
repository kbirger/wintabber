using System.Reactive;
using DynamicData;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Models;

namespace WinTabber.Api.Media.CoreAudio.Services;

/// <summary>
/// The seam over <see cref="AudioSessionService"/>, extracted verbatim from its public surface.
/// </summary>
public interface IAudioSessionService
{
    IObservableCache<CoreAudioSessionWrapper, string> CoreAudioSessions { get; }

    IObservableCache<SessionDto, string> Sessions { get; }

    IObservable<Unit> SetVolume(CoreAudioSessionWrapper session, float volume);

    IObservable<Unit> SetVolume(SessionDto session, float volume);

    IObservable<Unit> SetMute(CoreAudioSessionWrapper session, bool isMuted);

    IObservable<Unit> SetMute(SessionDto session, bool mute);
}
