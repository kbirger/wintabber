using DynamicData;
using NAudio.CoreAudioApi;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Models;
using WinTabber.Api.Media.CoreAudio.Repositories;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

namespace WinTabber.Api.Media.CoreAudio.Services;

public partial class AudioDeviceService(CoreAudioDeviceRepository repository) : IAudioDeviceService
{
    private readonly CoreAudioDeviceRepository _repository = repository;

    private IObservableCache<IAudioDevice, string> _nativeDevices = repository.Devices;
    private IObservableCache<DefaultDeviceChange, DefaultDeviceKey> _defaultDevices = repository
        .GetDefaultDevices()
        .AsObservableCache();

    public ObservableDeviceDto WatchDevice(IAudioDevice? device)
    {
        if (device == null)
        {
            return new ObservableDeviceDto
            {
                CanMute = false,
                CanSetVolume = false,
                DisplayName = string.Empty,
                Id = string.Empty,
                IsDefaultChanges = Observable.Empty<bool>(),
                PropertyChanges = Observable.Empty<PropertyKey>(),
                Removed = Observable.Empty<Unit>(),
                StateChanges = Observable.Empty<DeviceState>(),
                VolumeChanges = Observable.Empty<float>(),
                IsMutedChanges = Observable.Empty<bool>(),
                CanMuteChanges = Observable.Return(false),
                CanSetVolumeChanges = Observable.Return(false),
                SetVolume = (volume) => Observable.Empty<Unit>(),
                SetMute = (volume) => Observable.Empty<Unit>()
            };
        }

        var deviceEvents = _repository.Watch(device);
        var canSetVolume = device.CanSetVolume;
        var canMute = canSetVolume || device.CanMute;

        return new ObservableDeviceDto
        {
            CanMute = canMute,
            CanSetVolume = canSetVolume,
            DisplayName = device.DisplayName,
            Id = device.Id,

            IsDefaultChanges = deviceEvents.IsDefaultChanges,
            PropertyChanges = deviceEvents.PropertyChanges,
            Removed = deviceEvents.Removed,
            StateChanges = deviceEvents.StateChanges,
            VolumeChanges = deviceEvents
                .VolumeChanges
                .Throttle(TimeSpan.FromMilliseconds(100)),
            IsMutedChanges = deviceEvents.MuteChanges,
            CanMuteChanges = Observable.Return(device.CanMute),
            CanSetVolumeChanges = Observable.Return(device.CanSetVolume),
            SetVolume = device.SetVolume,
            SetMute = device.SetMute
        };
    }

    public IObservable<ObservableDeviceDto> WatchDevice(string deviceId)
    {
        return _nativeDevices
            .WatchValue(deviceId)
            .Select(WatchDevice)
            .SubscribeOn(_repository.Scheduler);
        //.ObserveOn(DefaultScheduler.Instance);
    }

    [Lazy]
    private IObservableCache<DeviceDto, string> GetDevices()
    {
        return _nativeDevices
            .Connect()
            .ObserveOn(_repository.Scheduler)
            .Transform(CreateItem)
            .AsObservableCache();
    }

    private static DeviceDto CreateItem(IAudioDevice data)
    {
        return new DeviceDto
        {
            DeviceId = data.Id,
            DeviceFriendlyName = data.DeviceFriendlyName,
            DeviceName = data.FriendlyName,
            DataFlow = data.DataFlow,
        };
    }

    public IObservable<DeviceDto> GetDefaultDevice(DataFlow dataFlow = DataFlow.All, Role role = Role.Multimedia)
    {
        // Watches Devices (the same IObservableCache<DeviceDto,string> that backs the selector's
        // ItemsSource) by id, rather than building a fresh DeviceDto via CreateItem as this used to.
        // DeviceDto has value equality (by DeviceId), so the old version worked fine bound to WPF's
        // ComboBox.SelectedItem, which matches via Equals -- but WinUI 3's Selector.SelectedItem
        // requires the exact same object reference as an item in ItemsSource, so a value-equal but
        // distinct instance silently fails to select anything. Confirmed live: Devices populated
        // correctly, but nothing was ever highlighted as selected.
        return _defaultDevices
            .Connect()
            .ObserveOn(_repository.Scheduler)
            .Watch(new DefaultDeviceKey(dataFlow, role))
            .Select(newDefault => Devices.WatchValue(newDefault.Current.DeviceId))
            .Switch()
            .DistinctUntilChanged(device => device.DeviceId);
    }

    public IObservable<Unit> SetVolume(string deviceId, float volume)
    {
        var nativeDevice = _nativeDevices.Lookup(deviceId);
        if (nativeDevice.HasValue)
        {
            return nativeDevice.Value.SetVolume(volume);
        }

        return Observable.Empty<Unit>();
    }

    public IObservable<Unit> SetMute(string deviceId, bool isMuted)
    {
        var nativeDevice = _nativeDevices.Lookup(deviceId);
        if (nativeDevice.HasValue)
        {
            return nativeDevice.Value.SetMute(isMuted);
        }
        return Observable.Empty<Unit>();
    }

    public IObservable<Unit> SetDefaultAudioEndpoint(string deviceId)
    {
        return SetDefaultAudioEndpoint(deviceId, [Role.Multimedia, Role.Communications, Role.Console]);
    }
    public IObservable<Unit> SetDefaultAudioEndpoint(string deviceId, params Role[] roles)
    {
        return _repository.SetDefaultAudioEndpoint(deviceId, roles);
    }
}
