using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.Api.Media.CoreAudio.Models;

public class DeviceEvents
{
    public required IObservable<DeviceState> StateChanges { get; init; }
    public required IObservable<Unit> Removed { get; init; }
    public required IObservable<PropertyKey> PropertyChanges { get; init; }

    public required IObservable<bool> IsDefaultChanges { get; init; }
    public required IObservable<AudioVolumeNotificationData> VolumeChanges { get; init; }
}
