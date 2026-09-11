using System.Reactive;
using DynamicData;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Models;
using WinTabber.Api.Media.CoreAudio.Services;

namespace WinTabber.UI.Media.Tests.Fakes;

/// <summary>
/// <see cref="ViewModels.MediaSessionViewModel"/>'s constructor stores this service but never
/// calls any of its members, so every member throws — a call site that starts using one should
/// fail loudly here rather than silently getting a default.
/// </summary>
public sealed class FakeAudioSessionService : IAudioSessionService
{
    public IObservableCache<CoreAudioSessionWrapper, string> CoreAudioSessions => throw new NotSupportedException();

    public IObservableCache<SessionDto, string> Sessions => throw new NotSupportedException();

    public IObservable<Unit> SetVolume(CoreAudioSessionWrapper session, float volume) =>
        throw new NotSupportedException();

    public IObservable<Unit> SetVolume(SessionDto session, float volume) => throw new NotSupportedException();

    public IObservable<Unit> SetMute(CoreAudioSessionWrapper session, bool isMuted) =>
        throw new NotSupportedException();

    public IObservable<Unit> SetMute(SessionDto session, bool mute) => throw new NotSupportedException();
}
