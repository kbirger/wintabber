using DynamicData;
using NAudio.CoreAudioApi;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Models;
using WinTabber.Api.Media.CoreAudio.Repositories;
using WinTabber.Api.Media.Repositories;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

namespace WinTabber.Api.Media.CoreAudio.Services;

public partial class AudioDeviceService(CoreAudioDeviceRepository repository)
{
    private readonly CoreAudioDeviceRepository _repository = repository;

    private IObservableCache<CoreAudioDeviceWrapper, string> _nativeDevices = repository.Devices;
    private IObservableCache<DefaultDeviceChange, DefaultDeviceKey> _defaultDevices = repository
        .GetDefaultDevices()
        .AsObservableCache();

    public ObservableDeviceDto WatchDevice(CoreAudioDeviceWrapper? device)
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
                MuteChanges = Observable.Empty<bool>()
            };
        }

        var deviceEvents = _repository.Watch(device.Device);
        var canSetVolume = device.CanSetVolume;
        var canMute = canSetVolume || device.CanMute;
        //var endpoint = device.Device.AudioEndpointVolume;

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
            MuteChanges = deviceEvents.MuteChanges
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

    private bool CanSetVolume(MMDevice device)
    {
        var range = device.AudioEndpointVolume.VolumeRange;
        return range.MaxDecibels > range.MinDecibels;
    }

    private bool CanMute(MMDevice device)
    {
        return device.AudioEndpointVolume.HardwareSupport.HasFlag(EEndpointHardwareSupport.Mute);
    }

    [Lazy]
    private IObservable<IChangeSet<DeviceDto, string>> GetDevices()
    {
        return _nativeDevices
            .Connect()
            .ObserveOn(_repository.Scheduler)
            .Transform(data =>
            {
                var isDefault = _defaultDevices.Lookup(new DefaultDeviceKey(data.DataFlow, Role.Multimedia));

                return CreateItem(data);
            })
            .Publish()
            .RefCount();
    }

    private static DeviceDto CreateItem(CoreAudioDeviceWrapper data)
    {
        return new DeviceDto
        {
            DeviceId = data.Id,
            //IsSelected = isDefault.HasValue && isDefault.Value.DeviceId == data.ID,
            DeviceFriendlyName = data.DeviceFriendlyName,
            DeviceName = data.FriendlyName,
            DataFlow = data.DataFlow,
        };
    }

    public IObservable<DeviceDto> GetDefaultDevice(DataFlow dataFlow = DataFlow.All, Role role = Role.Multimedia)
    {
        return _defaultDevices
            .Connect()
            .ObserveOn(_repository.Scheduler)
            .Watch(new DefaultDeviceKey(dataFlow, role))
            .Select(newDefault =>
                _nativeDevices
                    .Watch(newDefault.Current.DeviceId)
                    .Do(x =>
                    {
                        Debug.WriteLine($"default {x.Current.FriendlyName}");
                    })
                    .Select(change => change.Current)
                    .Do(x =>
                    {
                        Debug.WriteLine($"single default {x.FriendlyName}");
                    })
            )
            .Switch()
            .Select(CreateItem);
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
}
