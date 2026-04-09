using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Reactive;
using System.Text;
using WinTabber.Api.Media.CoreAudio.Models;

namespace WinTabber.Api.Media.CoreAudio.Dtos;

public class ObservableDeviceDto
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
    public required IObservable<bool> MuteChanges { get; init; }
}
