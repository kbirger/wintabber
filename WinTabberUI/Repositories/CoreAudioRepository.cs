using DynamicData;
using NAudio.CoreAudioApi;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTabberUI.Extensions;
using WinTabberUI.Models;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Repositories;

public partial class CoreAudioDeviceRepository : IDisposable
{
    MMDeviceEnumerator _enumerator = new MMDeviceEnumerator();

    public void Dispose()
    {
        try
        {
            _enumerator?.Dispose();
        }
        catch
        {

        }
    }

    //private MMDeviceCollection GetDevices()
    //{
    //    return _enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active);
    //}

    [Lazy]
    private EventLoopScheduler GetScheduler()
    {
        return new EventLoopScheduler(ts =>
        {
            var thread = new Thread(ts)
            {
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            return thread;
        });
    }

    [Lazy]
    private IObservable<IChangeSet<CoreAudioDeviceWrapper, string>> GetDevices()
    {
        return ObservableChangeSet.Create<CoreAudioDeviceWrapper, string>(cache =>
            {
                var dispose = new CompositeDisposable();
                var subscription = DevicesObservable
                    .Subscribe(devices =>
                    {
                        Debug.WriteLine($"Devices fetched on thread {Environment.CurrentManagedThreadId}");
                        var monitor = new CoreAudioDevicesMonitor(_enumerator);
                        cache.AddOrUpdate(
                            devices.Select(device => new CoreAudioDeviceWrapper(device, monitor.Watch(device.ID), Scheduler)));

                        var removalSubscription = monitor.DeviceRemovals
                            .Subscribe(deviceId =>
                            {
                                cache.Remove(deviceId);
                            });

                        var additionSubscription = monitor.DeviceAdditions
                            .Subscribe(deviceId =>
                            {
                                var device = _enumerator.GetDevice(deviceId);
                                if (device.State == DeviceState.Active)
                                {
                                    cache.AddOrUpdate(new CoreAudioDeviceWrapper(device, monitor.Watch(deviceId), Scheduler));
                                }

                            });

                        var stateChangeSubscription = monitor.DeviceStateChanges
                            .Where(change => change.NewState.In(DeviceState.Unplugged, DeviceState.Disabled, DeviceState.NotPresent))
                            .Subscribe(change =>
                            {
                                Debug.WriteLine($"Devices changed on thread {Environment.CurrentManagedThreadId}");

                                cache.Remove(change.DeviceId);
                            });

                        var stateChangeSubscription2 = monitor.DeviceStateChanges
                            .Where(change => change.NewState.In(DeviceState.Active))
                            .Subscribe(change =>
                            {
                                Debug.WriteLine($"Devices changed on thread {Environment.CurrentManagedThreadId}");

                                var device = _enumerator.GetDevice(change.DeviceId);
                                cache.AddOrUpdate(new CoreAudioDeviceWrapper(device, monitor.Watch(change.DeviceId), Scheduler));
                            });

                        var compositeDisposable = new CompositeDisposable
                        {
                            removalSubscription,
                            additionSubscription,
                            stateChangeSubscription,
                            stateChangeSubscription2
                        }.DisposeWith(dispose);
                    }).DisposeWith(dispose);
                return dispose;
            },
            device => device.ID
        ).AutoRefresh(device => device.State)
        .SubscribeOn(Scheduler);
    }

    [Lazy]
    private IObservable<IReadOnlyList<MMDevice>> GetDevicesObservable()
    {
        return Observable.Start<IReadOnlyList<MMDevice>>(() =>
        {
            var devices = _enumerator
                        .EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active)
                        .ToArray();

            return devices;
        }, Scheduler)
        // Replay last value for late subscribers
        .Replay(1)
        .RefCount();
        // Marshal notifications to UI thread
        //.ObserveOn(RxApp.MainThreadScheduler);
    }
    //[Lazy]
    //private IObservable<IReadOnlyList<MMDevice>> GetDevicesObservable()
    //{
    //    return Observable.Create<IReadOnlyList<MMDevice>>(observer =>
    //    {
    //        Task.Factory.StartNew(() =>
    //        {
    //            Debug.WriteLine($"Fetching devices from thread {Environment.CurrentManagedThreadId}");

    //            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active)
    //                .ToArray();
    //            observer.OnNext(devices);

    //            observer.OnCompleted();
    //        },
    //        CancellationToken.None,
    //        TaskCreationOptions.None,
    //        TaskScheduler.FromCurrentSynchronizationContext()
    //        );

    //        return () => { };
    //    })
    //    .Replay(1)
    //    .RefCount();
    //}
}
