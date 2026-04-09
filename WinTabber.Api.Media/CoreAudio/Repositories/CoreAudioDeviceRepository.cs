using DynamicData;
using NAudio.CoreAudioApi;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using WinTabber.Api.Media.CoreAudio.Models;
using WinTabber.Api.Media.CoreAudio.Repositories;
using WinTabber.Common.Util;

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

    public EventLoopScheduler Scheduler => STAScheduler.Default;

    public IObservableCache<DefaultDeviceChange, DefaultDeviceKey> GetDefaultDevices()
    {
        return Observable
            .Defer(() =>
            {
                return ObservableChangeSet.Create<DefaultDeviceChange, DefaultDeviceKey>(
                    cache =>
                    {
                        DataFlow[] flows = [DataFlow.Render, DataFlow.Capture];
                        Role[] roles = [Role.Console, Role.Multimedia, Role.Communications];
                        var currentDefaults =
                            from flow in flows
                            from role in roles
                            from change in CreateDefaultDeviceChange(flow, role)
                            select change;

                        cache.AddOrUpdate(currentDefaults);

                        var changes = _monitor.DefaultDeviceChanges.Subscribe(change =>
                        {
                            cache.AddOrUpdate(change);
                        });

                        return changes;
                    },
                    item => new DefaultDeviceKey(item.DataFlow, item.Role)
                );
            })
            .ObserveOn(Scheduler)
            .SubscribeOn(Scheduler)
            .AsObservableCache();
        ;
    }

    private IEnumerable<DefaultDeviceChange> CreateDefaultDeviceChange(DataFlow flow, Role role)
    {
        DefaultDeviceChange? change = null;
        try
        {
            var device = _enumerator.GetDefaultAudioEndpoint(flow, role);
            change = new DefaultDeviceChange(flow, role, device.ID);
        }
        catch (COMException) { }

        if(change is not null)
        {
            yield return change;
        }
    }

    public MMDevice? GetDefaultPlaybackDevice()
    {
        if (_enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
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
    private IObservableCache<MMDevice, string> GetDevices()
    {
        return ObservableChangeSet
            .Create<MMDevice, string>(
                cache =>
                {
                    var dispose = new CompositeDisposable(
                        Disposable.Create(() =>
                        {
                            Debug.WriteLine("disposing");
                        })
                    );
                    return Scheduler.Schedule(() =>
                    {
                        DevicesObservable
                            .Take(1)
                            .Subscribe(devices =>
                            {
                                Debug.WriteLine(
                                    $"Devices fetched on thread {Environment.CurrentManagedThreadId} - {Thread.CurrentThread.Name} - {Thread.CurrentThread.GetApartmentState()}"
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
                                    Debug.WriteLine(
                                        $"Device {change.DeviceId} is now default {change.DataFlow} device"
                                    );
                                    cache.Refresh();
                                });

                                var stateChangeSubscription = _monitor.DeviceStateChanges.Subscribe(change =>
                                {
                                    Debug.WriteLine(
                                        $"Device state changed on thread {Environment.CurrentManagedThreadId} - {change.NewState}"
                                    );
                                    if (
                                        change.NewState.In(
                                            DeviceState.Unplugged,
                                            DeviceState.Disabled,
                                            DeviceState.NotPresent
                                        )
                                    )
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
                    });
                },
                device => device.ID
            )
            .DisposeMany()
            .SubscribeOn(Scheduler)
            .ObserveOn(Scheduler)
            .AsObservableCache();
    }

    [Lazy(IsPrivate = true)]
    private IObservable<IReadOnlyList<MMDevice>> GetDevicesObservable()
    {
        return Observable
            .Start<IReadOnlyList<MMDevice>>(
                () =>
                {
                    var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active).ToArray();

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
