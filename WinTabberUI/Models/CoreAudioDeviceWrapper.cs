using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.CoreAudioApi;
using WinTabberUI.Repositories;

namespace WinTabberUI.Models;

[DebuggerDisplay("Device: {FriendlyName}, State: {State}, IsDefault: {IsDefault}")]
public class CoreAudioDeviceWrapper : ObservableObject, IDisposable
{
    private Dispatcher Dispatcher { get; }

    private readonly MMDevice _device;
    private readonly EventLoopScheduler _scheduler;
    private readonly IDisposable _subscriptions;

    public CoreAudioDeviceWrapper(
        MMDevice device,
        MMDeviceEnumerator enumerator,
        CoreAudioDeviceMonitor monitor,
        EventLoopScheduler scheduler
    )
    {
        Dispatcher = Dispatcher.CurrentDispatcher;
        _device = device;
        State = device.State;
        DataFlow = device.DataFlow;
        ID = device.ID;
        InstanceId = device.InstanceId;
        IconPath = device.IconPath;
        DeviceFriendlyName = device.DeviceFriendlyName;
        FriendlyName = device.FriendlyName;
        Properties = device.Properties;
        IsDefault = enumerator.GetDefaultAudioEndpoint(device.DataFlow, Role.Multimedia)?.ID == device.ID;
        Volume = device.AudioEndpointVolume.MasterVolumeLevelScalar;
        IsMuted = device.AudioEndpointVolume.Mute;

        _scheduler = scheduler;
        var stateChanges = monitor.StateChanges.ObserveOn(scheduler).Subscribe(state => UpdateState(state));

        var removed = monitor.Removed.ObserveOn(scheduler).Subscribe(_ => UpdateState());

        var isDefault = monitor.IsDefaultChanges.ObserveOn(scheduler).Subscribe(UpdateDefault);

        //var volumechangeSubscription = volumeChanges.ObserveOn(scheduler).Subscribe(UpdateVolume);

        //var propertyChanges = monitor.PropertyChanges
        //    .Subscribe(_ => OnPropertyChanged(nameof(Properties)));

        _subscriptions = new CompositeDisposable(stateChanges, removed);
    }

    public void SetVolume(float volume)
    {
        Volume = volume;
        Dispatcher.BeginInvoke(() => _device.AudioEndpointVolume.MasterVolumeLevelScalar = volume);
    }

    public void SetMute(bool isMuted)
    {
        IsMuted = isMuted;
        Dispatcher.BeginInvoke(() => _device.AudioEndpointVolume.Mute = isMuted);
    }

    private void UpdateVolume(AudioVolumeNotificationData data)
    {
        Debug.WriteLine($"Device volume change on thread {Environment.CurrentManagedThreadId}");
        Dispatcher.Invoke(() =>
        {
            Debug.WriteLine($"Updating device volume property on thread {Environment.CurrentManagedThreadId}");

            IsMuted = data.Muted;
            Volume = data.MasterVolume;
        });

    }

    private void UpdateDefault(bool isDefault)
    {
        Debug.WriteLine($"Device default change on thread {Environment.CurrentManagedThreadId}");

        Dispatcher.Invoke(() =>
        {
            Debug.WriteLine($"Updating device default property on thread {Environment.CurrentManagedThreadId}");
            IsDefault = isDefault;
        });
    }

    private void UpdateState(DeviceState? newState = null)
    {
        Debug.WriteLine($"Device state change on thread {Environment.CurrentManagedThreadId}");

        Dispatcher.BeginInvoke(() =>
        {
            Debug.WriteLine($"Updating device state property on thread {Environment.CurrentManagedThreadId}");

            if (newState.HasValue)
            {
                State = newState.Value;
            }
            else
            {
                State = _device.State;
            }
        });
    }

    public MMDevice Device => _device;
    public DeviceState State
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public DataFlow DataFlow
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public string ID
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public string InstanceId
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public string IconPath
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public string DeviceFriendlyName
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public string FriendlyName
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public PropertyStore Properties
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public bool IsDefault
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public float Volume 
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public bool IsMuted
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }
}
