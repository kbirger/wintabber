using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;

namespace WinTabberUI.Repositories;

public class CoreAudioDeviceMonitor
{
    public required IObservable<DeviceState> StateChanges { get; init; }
    public required IObservable<Unit> Removed { get; init; }
    public required IObservable<PropertyKey> PropertyChanges { get; init; }

    public required IObservable<bool> IsDefaultChanges { get; init; }
}
