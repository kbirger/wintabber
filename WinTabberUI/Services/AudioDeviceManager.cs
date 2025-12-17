using CoreAudio;
using DynamicData;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Services;

public partial class AudioDeviceManager : IAudioDeviceManager, IDisposable
{

    MMDeviceEnumerator _enumerator = new MMDeviceEnumerator(Guid.NewGuid());
    MMNotificationClient _notificationClient;
    private readonly SourceCache<DeviceItem, string> _devices;
    public AudioDeviceManager()
    {
        _notificationClient = new MMNotificationClient(_enumerator);
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
        return Observable.Create<IChangeSet<DeviceItem, string>>(observer =>
        {
            var connection = _devices.Connect();
            var disposables = new CompositeDisposable();


            DevicesObservable
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(devices =>
                {
                    _devices.Edit(updater =>
                    {
                        updater.Clear();
                        updater.AddOrUpdate(devices);
                    });

                })
                .DisposeWith(disposables);

            DeviceAdditions
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(added =>
                {
                    _devices.AddOrUpdate(new DeviceItem(_enumerator.GetDevice(added.DeviceId), _enumerator));
                })
                .DisposeWith(disposables);

            DeviceRemovals
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(removed =>
                {
                    _devices.Remove(removed.DeviceId);
                })
                .DisposeWith(disposables);

            connection.Subscribe(devices => observer.OnNext(devices))
                .DisposeWith(disposables);

            return () =>
            {
                disposables.Dispose();
            };
        }).Replay(1).RefCount();
    }

    [Lazy]
    private IObservable<DefaultDeviceChangedEventArgs> GetDefaultDeviceChanges()
    {
        return Observable.FromEventPattern<DefaultDeviceChangedEventArgs>(
            h => _notificationClient.DefaultDeviceChanged += h,
            h => _notificationClient.DefaultDeviceChanged -= h)
            .Select(x => x.EventArgs);
    }

    [Lazy]
    private IObservable<DeviceNotificationEventArgs> GetDeviceAdditions()
    {
        return Observable.FromEventPattern<DeviceNotificationEventArgs>(
            h => _notificationClient.DeviceAdded += h,
            h => _notificationClient.DeviceAdded -= h)
            .Select(x => x.EventArgs);
    }

    [Lazy]
    private IObservable<DeviceNotificationEventArgs> GetDeviceRemovals()
    {
        return Observable.FromEventPattern<DeviceNotificationEventArgs>(
            h => _notificationClient.DeviceRemoved += h,
            h => _notificationClient.DeviceRemoved -= h)
            .Select(x => x.EventArgs);
    }

    [Lazy]
    private IObservable<DefaultDeviceChangedEventArgs> GetDefaultPlaybackDeviceChanges()
    {
        return DefaultDeviceChanges
            .Where(e => e.DataFlow == DataFlow.Render);
    }

    [Lazy]
    private IObservable<DefaultDeviceChangedEventArgs> GetDefaultRecordingDeviceChanges()
    {
        return DefaultDeviceChanges
            .Where(e => e.DataFlow == DataFlow.Capture);
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

}
