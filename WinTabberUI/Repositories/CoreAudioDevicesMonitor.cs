using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace WinTabberUI.Repositories;

public class CoreAudioDevicesMonitor : IMMNotificationClient, IDisposable
{
    private Subject<(string DeviceId, DeviceState NewState)> _deviceStateChanges = new();
    private Subject<string> _deviceAdditions = new();
    private Subject<string> _deviceRemovals = new();
    private Subject<(DataFlow Flow, Role Role, string DeviceId)> _defaultDeviceChanges = new();
    private Subject<(string DeviceId, NAudio.CoreAudioApi.PropertyKey Key)> _devicePropertyChanges = new();
    private readonly MMDeviceEnumerator _enumerator;

    public IObservable<(string DeviceId, DeviceState NewState)> DeviceStateChanges => _deviceStateChanges;

    public IObservable<string> DeviceAdditions => _deviceAdditions;
    public IObservable<string> DeviceRemovals => _deviceRemovals;
    public IObservable<(DataFlow Flow, Role Role, string DeviceId)> DefaultDeviceChanges => _defaultDeviceChanges;
    public IObservable<(string DeviceId, NAudio.CoreAudioApi.PropertyKey Key)> DevicePropertyChanges => _devicePropertyChanges;

    public CoreAudioDevicesMonitor(MMDeviceEnumerator enumerator)
    {
        enumerator.RegisterEndpointNotificationCallback(this);
        _enumerator = enumerator;
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
        _defaultDeviceChanges.OnNext((flow, role, defaultDeviceId));
    }

    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        _devicePropertyChanges.OnNext((pwstrDeviceId, key));
    }

    public IMMNotificationClient AsNotificationClient()
    {
        return this;
    }

    public CoreAudioDeviceMonitor Watch(string deviceId)
    {
        return new CoreAudioDeviceMonitor
        {
            PropertyChanges = DevicePropertyChanges
                .Where(change => change.DeviceId == deviceId)
                .Select(change => change.Key),
            Removed = DeviceRemovals
                .Where(removedId => removedId == deviceId)
                .Select(_ => Unit.Default),
            StateChanges = DeviceStateChanges
                .Where(change => change.DeviceId == deviceId)
                .Select(change => change.NewState)
        };
    }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(this);
    }
}
