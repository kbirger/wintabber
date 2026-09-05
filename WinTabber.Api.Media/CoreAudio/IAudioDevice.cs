using System.Reactive;
using NAudio.CoreAudioApi;

namespace WinTabber.Api.Media.CoreAudio;

/// <summary>
/// Abstraction over NAudio's <see cref="MMDevice"/>, whose only constructor is internal and takes
/// an internal COM interface — no test code can construct one. Cached members mirror fields that
/// are safe to read off the STA thread the underlying device was created on (copied out at
/// construction, same as before this interface existed); the rest read through live, exactly as
/// today's code reads them off <c>MMDevice</c>/<c>AudioEndpointVolume</c> directly.
/// </summary>
public interface IAudioDevice
{
    string Id { get; }
    DataFlow DataFlow { get; }
    string DisplayName { get; }
    string FriendlyName { get; }
    string DeviceFriendlyName { get; }
    bool CanSetVolume { get; }
    bool CanMute { get; }
    DeviceState State { get; }

    float MasterVolumeLevelScalar { get; set; }
    bool Mute { get; set; }
    IObservable<(float MasterVolume, bool Muted)> VolumeChanged { get; }

    /// <summary>
    /// Not fakeable in tests — COM-backed, no accessible constructor, same limitation
    /// <see cref="MMDevice"/> itself had. Consumed only by <c>CoreAudioSessionRepository</c>,
    /// which stays untested for the same reason.
    /// </summary>
    AudioSessionManager AudioSessionManager { get; }

    IObservable<Unit> SetVolume(float volume);
    IObservable<Unit> SetMute(bool isMuted);
}
