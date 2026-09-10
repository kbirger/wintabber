using System.Reactive;
using DynamicData;
using NAudio.CoreAudioApi;
using WinTabber.Api.Media.CoreAudio.Dtos;

namespace WinTabber.Api.Media.CoreAudio.Services;

/// <summary>
/// The seam over <see cref="AudioDeviceService"/>, extracted verbatim from its public surface.
/// </summary>
public interface IAudioDeviceService
{
    /// <summary>Generated from <c>GetDevices()</c> by the <c>[Lazy]</c> source generator.</summary>
    IObservableCache<DeviceDto, string> Devices { get; }

    ObservableDeviceDto WatchDevice(IAudioDevice? device);

    IObservable<ObservableDeviceDto> WatchDevice(string deviceId);

    IObservable<DeviceDto> GetDefaultDevice(DataFlow dataFlow = DataFlow.All, Role role = Role.Multimedia);

    IObservable<Unit> SetVolume(string deviceId, float volume);

    IObservable<Unit> SetMute(string deviceId, bool isMuted);

    IObservable<Unit> SetDefaultAudioEndpoint(string deviceId);

    IObservable<Unit> SetDefaultAudioEndpoint(string deviceId, params Role[] roles);
}
