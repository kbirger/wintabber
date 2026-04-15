using NAudio.CoreAudioApi;
using System.Reactive;

namespace WinTabber.Api.Media.CoreAudio.Models;

public class DeviceEvents
{
    public required IObservable<DeviceState> StateChanges { get; init; }
    public required IObservable<Unit> Removed { get; init; }
    public required IObservable<PropertyKey> PropertyChanges { get; init; }

    public required IObservable<bool> IsDefaultChanges { get; init; }
    public required IObservable<float> VolumeChanges { get; init; }
    public required IObservable<bool> MuteChanges { get; init; }
}
