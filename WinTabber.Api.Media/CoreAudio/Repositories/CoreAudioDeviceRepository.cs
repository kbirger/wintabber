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
using WinTabber.Api.Media.CoreAudio.Models;
using WinTabber.Api.Media.CoreAudio.Repositories;

namespace WinTabber.Api.Media.Repositories;


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

    public IObservable<IChangeSet<DefaultDeviceChange, DefaultDeviceKey>> GetDefaultDevices()
    {
        return _monitor.DefaultDeviceChanges.ToObservableChangeSet((change) => new DefaultDeviceKey(change.Flow, change.Role));
    }

    


    public MMDevice? GetDefaultPlaybackDevice()
    {
        if(_enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        return null;
    }

    public MMDevice? GetDefaultRecordingDevice()
    {
        if (_enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia))
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        }
        return null;
    }

    public DeviceEvents Watch(MMDevice device)
    {
        return _monitor.Watch(device);
    }

    [Lazy]
    private IObservable<IChangeSet<MMDevice, string>> GetDevices()
    {
        return ObservableChangeSet
            .Create<MMDevice, string>(
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
                            cache.AddOrUpdate(devices);

                            var removalSubscription = _monitor.DeviceRemovals.Subscribe(deviceId =>
                            {
                                cache.Remove(deviceId);
                            });

                            var additionSubscription = _monitor.DeviceAdditions.Subscribe(deviceId =>
                            {
                                var device = _enumerator.GetDevice(deviceId);
                                if (device.State == DeviceState.Active)
                                {
                                    cache.AddOrUpdate(device);
                                    cache.Refresh(device);
                                }
                            });
                            var defaultSubscription = _monitor.DefaultDeviceChanges.Subscribe(change =>
                            {
                                Debug.WriteLine($"Device {change.DeviceId} is now default {change.Flow} device");
                                cache.Refresh();
                            });

                            var stateChangeSubscription = _monitor.DeviceStateChanges.Subscribe(change =>
                            {
                                Debug.WriteLine(
                                    $"Device state changed on thread {Environment.CurrentManagedThreadId} - {change.NewState}"
                                );
                                if (change.NewState.In(DeviceState.Unplugged, DeviceState.Disabled, DeviceState.NotPresent))
                                {
                                    Debug.WriteLine(
                                        $"Device state changed on thread {Environment.CurrentManagedThreadId} - {change.NewState}"
                                    );
                                    cache.Remove(change.DeviceId);
                                }
                                else if (change.NewState == DeviceState.Active)
                                {
                                    var device = _enumerator.GetDevice(change.DeviceId);

                                    cache.AddOrUpdate(device);
                                    cache.Refresh(device);

                                }
                            });


                            var compositeDisposable = new CompositeDisposable
                            {
                                removalSubscription,
                                additionSubscription,
                                stateChangeSubscription,
                            }.DisposeWith(dispose);
                        })
                        .DisposeWith(dispose);
                    return dispose;
                },
                device => device.ID
            )
            
            .DisposeMany()
            .SubscribeOn(Scheduler)
            .ObserveOn(Scheduler)
            .Publish()
            .AutoConnect();
            ;
    }

    [Lazy(IsPrivate = true)]
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
