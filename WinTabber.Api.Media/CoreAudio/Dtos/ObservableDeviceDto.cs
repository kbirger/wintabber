using NAudio.CoreAudioApi;
using System.Reactive;

namespace WinTabber.Api.Media.CoreAudio.Dtos;

public class ObservableDeviceDto : IObservableVolumeDto
{
    public required string DisplayName { get; init; }
    public required string Id { get; init; }

    public required bool CanSetVolume { get; init; }

    public required bool CanMute { get; init; }
    public required IObservable<DeviceState> StateChanges { get; init; }
    public required IObservable<Unit> Removed { get; init; }
    public required IObservable<PropertyKey> PropertyChanges { get; init; }

    public required IObservable<bool> IsDefaultChanges { get; init; }
    public required IObservable<float> VolumeChanges { get; init; }
    public required IObservable<bool> IsMutedChanges { get; init; }

    public required IObservable<bool> CanMuteChanges { get; init; }
    public required IObservable<bool> CanSetVolumeChanges { get; init; }

    public required Func<float, IObservable<Unit>> SetVolume { get; init; }
    public required Func<bool, IObservable<Unit>> SetMute { get; init; }
}
