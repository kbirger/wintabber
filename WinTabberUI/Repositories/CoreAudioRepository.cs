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
using DynamicData;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using ReactiveUI;
using WinTabberUI.Extensions;
using WinTabberUI.Models;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Repositories;

public partial class CoreAudioDeviceRepository : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly CoreAudioDevicesMonitor _monitor;

    public CoreAudioDeviceRepository()
    {
         _enumerator = new MMDeviceEnumerator();

         _monitor = new CoreAudioDevicesMonitor(_enumerator, Scheduler);
    }
    public void Dispose()
    {
        try
        {
            _enumerator?.Dispose();
            _monitor?.Dispose();
        }
        catch { }
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
            var thread = new Thread(ts) { IsBackground = true };
            Debug.WriteLine($"Creating eventloop STA thread: {thread.ManagedThreadId}");
            thread.Name = "CoreAudioWorker";
            thread.SetApartmentState(ApartmentState.STA);
            return thread;
        });
    }
    private CoreAudioDeviceWrapper CreateDeviceWrapper(MMDevice device)
    {
        return new CoreAudioDeviceWrapper(
            device,
            _enumerator,
            _monitor.Watch(device.ID),
            Scheduler
        );
    }

    
    [Lazy]
    private IObservable<IChangeSet<CoreAudioDeviceWrapper, string>> GetDevices()
    {
        return ObservableChangeSet
            .Create<CoreAudioDeviceWrapper, string>(
                cache =>
                {
                    var dispose = new CompositeDisposable(Disposable.Create(() =>
                    {
                        Debug.WriteLine("disposing");
                    }));
                    var subscription = DevicesObservable
                        .Take(1)
                        .Subscribe(devices =>
                        {
                            Debug.WriteLine(
                                $"Devices fetched on thread {Environment.CurrentManagedThreadId}"
                            );
                            cache.AddOrUpdate(devices.Select(CreateDeviceWrapper));

                            var removalSubscription = _monitor.DeviceRemovals.Subscribe(deviceId =>
                            {
                                cache.Remove(deviceId);
                            });

                            var additionSubscription = _monitor.DeviceAdditions.Subscribe(deviceId =>
                            {
                                var device = _enumerator.GetDevice(deviceId);
                                if (device.State == DeviceState.Active)
                                {
                                    cache.AddOrUpdate(CreateDeviceWrapper(device));
                                }
                            });

                            var deviceDeactivationSubscription = _monitor
                                .DeviceStateChanges.Where(change =>
                                    change.NewState.In(
                                        DeviceState.Unplugged,
                                        DeviceState.Disabled,
                                        DeviceState.NotPresent
                                    )
                                )
                                .Subscribe(change =>
                                {
                                    Debug.WriteLine(
                                        $"Device state changed on thread {Environment.CurrentManagedThreadId} - {change.NewState}"
                                    );

                                    cache.Remove(change.DeviceId);
                                });

                            var stateChangeSubscription2 = _monitor
                                .DeviceStateChanges.Where(change =>
                                    change.NewState.In(DeviceState.Active)
                                )
                                .Subscribe(change =>
                                {
                                    Debug.WriteLine(
                                        $"Device state changed on thread {Environment.CurrentManagedThreadId} - {change.NewState}"
                                    );

                                    var device = _enumerator.GetDevice(change.DeviceId);
                                    cache.AddOrUpdate(CreateDeviceWrapper(device));
                                });

                            var compositeDisposable = new CompositeDisposable
                            {
                                removalSubscription,
                                additionSubscription,
                                deviceDeactivationSubscription,
                                stateChangeSubscription2,
                            }.DisposeWith(dispose);
                        })
                        .DisposeWith(dispose);
                    return dispose;
                },
                device => device.ID
            )
            .AutoRefresh(device => device.State)
            .DisposeMany()
            .SubscribeOn(Scheduler)
            .ObserveOn(Scheduler)
            .Publish()
            .AutoConnect();
            ;
    }

    [Lazy]
    private IObservable<IReadOnlyList<MMDevice>> GetDevicesObservable()
    {
        return Observable
            .Start<IReadOnlyList<MMDevice>>(
                () =>
                {
                    var devices = _enumerator
                        .EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active)
                        .ToArray();

                    return devices;
                },
                Scheduler
            )
            // Replay last value for late subscribers
            .Replay(1)
            .RefCount()
            .ObserveOn(Scheduler);
        // Marshal notifications to UI thread
        //.ObserveOn(RxSchedulers.MainThreadScheduler);
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
