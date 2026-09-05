using NAudio.CoreAudioApi;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using WinTabber.Api.Media.CoreAudio;

namespace WinTabber.Api.Media.Tests.Fakes;

/// <summary>
/// Freely constructible fake for <see cref="IAudioDevice"/> — unlike NAudio's <see cref="MMDevice"/>,
/// nothing here is COM-backed except <see cref="AudioSessionManager"/>, which stays unfakeable for
/// the same reason <see cref="FakeMMDeviceEnumeratorWrapper"/>'s device-returning members used to.
/// </summary>
public sealed class FakeAudioDevice : IAudioDevice
{
    public required string Id { get; init; }
    public DataFlow DataFlow { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = string.Empty;
    public string DeviceFriendlyName { get; init; } = string.Empty;
    public bool CanSetVolume { get; init; }
    public bool CanMute { get; init; }
    public DeviceState State { get; init; } = DeviceState.Active;

    public float MasterVolumeLevelScalar { get; set; }
    public bool Mute { get; set; }

    public Subject<(float MasterVolume, bool Muted)> VolumeChangedSubject { get; } = new();
    public IObservable<(float MasterVolume, bool Muted)> VolumeChanged => VolumeChangedSubject;

    public AudioSessionManager AudioSessionManager =>
        throw new NotSupportedException(
            "AudioSessionManager cannot be constructed by test code (COM-backed, no accessible constructor)."
        );

    public IObservable<Unit> SetVolume(float volume)
    {
        MasterVolumeLevelScalar = volume;
        return Observable.Return(Unit.Default);
    }

    public IObservable<Unit> SetMute(bool isMuted)
    {
        Mute = isMuted;
        return Observable.Return(Unit.Default);
    }
}
