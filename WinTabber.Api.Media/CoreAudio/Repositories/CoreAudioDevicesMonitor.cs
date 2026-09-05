using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using WinTabber.Api.Media.CoreAudio;
using WinTabber.Api.Media.CoreAudio.Models;

namespace WinTabber.Api.Media.CoreAudio.Repositories;

public record DefaultDeviceChange(DataFlow DataFlow, Role Role, string DeviceId);

public record DefaultDeviceKey(DataFlow Flow, Role Role);

public class CoreAudioDevicesMonitor : IMMNotificationClient, IDisposable
{
    private Subject<(string DeviceId, DeviceState NewState)> _deviceStateChanges = new();
    private Subject<string> _deviceAdditions = new();
    private Subject<string> _deviceRemovals = new();
    private Subject<DefaultDeviceChange> _defaultDeviceChanges = new();
    private Subject<(string DeviceId, NAudio.CoreAudioApi.PropertyKey Key)> _devicePropertyChanges = new();
    private readonly IMMDeviceEnumeratorWrapper _enumerator;
    private readonly IScheduler _scheduler;

    public IObservable<(string DeviceId, DeviceState NewState)> DeviceStateChanges =>
        _deviceStateChanges.ObserveOn(_scheduler);

    public IObservable<string> DeviceAdditions => _deviceAdditions.ObserveOn(_scheduler);
    public IObservable<string> DeviceRemovals => _deviceRemovals.ObserveOn(_scheduler);
    public IObservable<DefaultDeviceChange> DefaultDeviceChanges =>
        _defaultDeviceChanges.DistinctUntilChanged().ObserveOn(_scheduler);
    public IObservable<(string DeviceId, NAudio.CoreAudioApi.PropertyKey Key)> DevicePropertyChanges =>
        _devicePropertyChanges.ObserveOn(_scheduler);

    public CoreAudioDevicesMonitor(IMMDeviceEnumeratorWrapper enumerator, IScheduler scheduler)
    {
        enumerator.RegisterEndpointNotificationCallback(this);
        _enumerator = enumerator;
        _scheduler = scheduler;
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        _deviceStateChanges.OnNext((deviceId, newState));
    }

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId)
    {
        _deviceAdditions.OnNext(pwstrDeviceId);
    }

    void IMMNotificationClient.OnDeviceRemoved(string deviceId)
    {
        _deviceRemovals.OnNext(deviceId);
    }

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        _defaultDeviceChanges.OnNext(new(flow, role, defaultDeviceId));
    }

    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        _devicePropertyChanges.OnNext((pwstrDeviceId, key));
    }

    public IMMNotificationClient AsNotificationClient()
    {
        return this;
    }

    public DeviceEvents Watch(IAudioDevice device)
    {
        var initialValues = Observable.Start(() => (device.MasterVolumeLevelScalar, device.Mute), _scheduler);
        var volumeChanged = initialValues
            .Concat(device.VolumeChanged)
            .Replay(1)
            .RefCount();
        return new DeviceEvents
        {
            VolumeChanges = volumeChanged.Select(change => change.Item1),
            MuteChanges = volumeChanged.Select(change => change.Item2),
            PropertyChanges = GetPropertyChanges(device),
            Removed = GetRemoved(device),
            StateChanges = GetStateChanges(device),
            IsDefaultChanges = GetIsDefaultChanges(device),
        };
    }

    private IObservable<bool> GetIsDefaultChanges(IAudioDevice device)
    {
        return DefaultDeviceChanges.ObserveOn(_scheduler).Select(change => change.DeviceId == device.Id);
    }

    private IObservable<DeviceState> GetStateChanges(IAudioDevice device)
    {
        return DeviceStateChanges
            .ObserveOn(_scheduler)
            .Where(change => change.DeviceId == device.Id)
            .Select(change => change.NewState);
    }

    private IObservable<Unit> GetRemoved(IAudioDevice device)
    {
        return DeviceRemovals
            .ObserveOn(_scheduler)
            .Where(removedId => removedId == device.Id)
            .Select(_ => Unit.Default);
    }

    private IObservable<PropertyKey> GetPropertyChanges(IAudioDevice device)
    {
        return DevicePropertyChanges
            .ObserveOn(_scheduler)
            .Where(change => change.DeviceId == device.Id)
            .Select(change => change.Key);
    }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(this);
    }
}
