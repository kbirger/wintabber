using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.CoreAudioApi;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using WinTabberUI.Repositories;

namespace WinTabberUI.Models;

public class CoreAudioDeviceWrapper : ObservableObject, IDisposable
{
    private readonly MMDevice _device;
    private readonly EventLoopScheduler _scheduler;
    private readonly IDisposable _subscriptions;
    public CoreAudioDeviceWrapper(MMDevice device, CoreAudioDeviceMonitor monitor, System.Reactive.Concurrency.EventLoopScheduler scheduler)
    {
        _device = device;
        _scheduler = scheduler;
        var stateChanges = monitor.StateChanges
            .Subscribe(_ =>
            {
                Debug.WriteLine($"Device state change on thread {Environment.CurrentManagedThreadId}");
                OnPropertyChanged(nameof(State));
            });

        var removed = monitor.Removed
            .Subscribe(_ => OnPropertyChanged(nameof(State)));

        var propertyChanges = monitor.PropertyChanges
            .Subscribe(_ => OnPropertyChanged(nameof(Properties)));

        _subscriptions = new CompositeDisposable(stateChanges, removed, propertyChanges);
    }

    public MMDevice Device => _device;
    public DeviceState State => _device.State;

    public DataFlow DataFlow => _device.DataFlow;

    public string ID => _device.ID;

    public string InstanceId => _device.InstanceId;

    public string IconPath => _device.IconPath;

    public string DeviceFriendlyName => _device.DeviceFriendlyName;

    public string FriendlyName => _device.FriendlyName;

    public PropertyStore Properties => _device.Properties;


    public void Dispose()
    {
        _subscriptions.Dispose();
    }
}
