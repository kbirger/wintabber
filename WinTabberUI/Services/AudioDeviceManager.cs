using DynamicData;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using WinTabberUI.Extensions;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Services;

public partial class AudioDeviceManager : IAudioDeviceManager, IDisposable, IMMNotificationClient
{

    MMDeviceEnumerator _enumerator = new MMDeviceEnumerator();
    private readonly SourceCache<DeviceItem, string> _devices;
    public AudioDeviceManager()
    {
        //_notificationClient = new MMNotificationClient(_enumerator);
        _devices = new SourceCache<DeviceItem, string>(device => device.Id);
    }

    private MMDeviceCollection GetDevices()
    {
        return _enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active);
    }

    public IDisposable Init()
    {
        DevicesObservable.Take(1).Subscribe();
        return this;
    }
    public IObservable<IChangeSet<DeviceItem, string>> Connect()
    {
        return ObservableChangeSet.Create<DeviceItem, string>(observableCache =>
        {
            var connection = _devices.Connect();
            var disposables = new CompositeDisposable();


            DevicesObservable
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(devices =>
                {
                    observableCache.Edit(updater =>
                    {
                        updater.Clear();
                        updater.AddOrUpdate(devices);
                    });

                })
                .DisposeWith(disposables);



            var deviceDisabledEvents = _deviceStateChanges
                .Where(change => change.NewState.In(DeviceState.Unplugged, DeviceState.NotPresent, DeviceState.Disabled))
                .Select(change => change.DeviceId); ;

            var deviceEnabledEvents = _deviceStateChanges
                .Where(change => change.NewState == DeviceState.Active)
                .Select(change => change.DeviceId);

            _deviceAdditions
                .Merge(deviceEnabledEvents)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Select(id => new DeviceItem(_enumerator.GetDevice(id), _enumerator))
                .Subscribe(observableCache.AddOrUpdate)
                .DisposeWith(disposables);

            _deviceRemovals
                .Merge(deviceDisabledEvents)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(observableCache.Remove)
                .DisposeWith(disposables);

            return () =>
            {
                disposables.Dispose();
            };
        }, device => device.Id).Replay(1).RefCount();
    }




    [Lazy]
    public IObservable<DeviceItem[]> GetDevicesObservable()
    {
        var obs = Observable.Create<DeviceItem[]>(observer =>
        {
            Task.Factory.StartNew(() =>
            {

                Debug.WriteLine("Fetching devices from thread");
                var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active)
                    .Select(device => new DeviceItem(device, _enumerator))
                    .ToArray();
                observer.OnNext(devices);

                observer.OnCompleted();
            }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());

            return () => { };
        })
        .Replay(1);
        obs.Connect();
        return obs;

    }

    public void Dispose()
    {

    }

    private Subject<(string DeviceId, DeviceState NewState)> _deviceStateChanges = new();
    private Subject<string> _deviceAdditions = new();
    private Subject<string> _deviceRemovals = new();
    private Subject<(DataFlow Flow, Role Role, string DeviceId)> _defaultDeviceChanges = new();
    private Subject<(string DeviceId, NAudio.CoreAudioApi.PropertyKey Key)> _devicePropertyChanges = new();

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        _deviceStateChanges.OnNext((deviceId, newState));
    }

    public void OnDeviceAdded(string pwstrDeviceId)
    {
        _deviceAdditions.OnNext(pwstrDeviceId);
    }

    public void OnDeviceRemoved(string deviceId)
    {
        _deviceRemovals.OnNext(deviceId);
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        _defaultDeviceChanges.OnNext((flow, role, defaultDeviceId));
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, NAudio.CoreAudioApi.PropertyKey key)
    {
        _devicePropertyChanges.OnNext((pwstrDeviceId, key));
    }
}
