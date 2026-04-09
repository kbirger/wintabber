using DynamicData;
using NAudio.CoreAudioApi;
using System.Diagnostics;
using System.Reactive.Linq;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Repositories;
using WinTabber.Api.Media.Repositories;

namespace WinTabber.Api.Media.CoreAudio.Services;

public partial class AudioDeviceService(CoreAudioDeviceRepository repository)
{
    private readonly CoreAudioDeviceRepository _repository = repository;

    private IObservableCache<MMDevice, string> _nativeDevices = repository.Devices;
    private IObservableCache<DefaultDeviceChange, DefaultDeviceKey> _defaultDevices = repository.GetDefaultDevices().AsObservableCache();

    public IObservable<ObservableDeviceDto> WatchDevice(string deviceId)
    {

        return _nativeDevices.WatchValue(deviceId)
            .Select(device =>
            {
                var deviceEvents = _repository.Watch(device);
                var canSetVolume = CanSetVolume(device);
                var canMute = canSetVolume || CanMute(device);
                return new ObservableDeviceDto
                {
                    CanMute = canMute,
                    CanSetVolume = canSetVolume,
                    DisplayName = device.DeviceFriendlyName ?? device.FriendlyName,
                    Id = device.ID,
                    IsDefaultChanges = deviceEvents.IsDefaultChanges,
                    PropertyChanges = deviceEvents.PropertyChanges,
                    Removed = deviceEvents.Removed,
                    StateChanges = deviceEvents.StateChanges,
                    VolumeChanges = deviceEvents.VolumeChanges.Select(change => change.MasterVolume),
                    MuteChanges = deviceEvents.VolumeChanges.Select(change => change.Muted)
                };
            })
            .SubscribeOn(_repository.Scheduler)
            .ObserveOn(_repository.Scheduler);
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

    private static DeviceDto CreateItem(MMDevice data)
    {
        return new DeviceDto
        {
            DeviceId = data.ID,
            //IsSelected = isDefault.HasValue && isDefault.Value.DeviceId == data.ID,
            DeviceFriendlyName = data.DeviceFriendlyName,
            DeviceName = data.FriendlyName,
            DataFlow = data.DataFlow
        };
    }


    public IObservable<DeviceDto> GetDefaultDevice(DataFlow dataFlow = DataFlow.All, Role role = Role.Multimedia)
    {
        return _defaultDevices
            .Connect()
            .ObserveOn(_repository.Scheduler)
            .Watch(new DefaultDeviceKey(dataFlow, role))
            .Select(newDefault => _nativeDevices
                .Watch(newDefault.Current.DeviceId)
                .Do(x => { Debug.WriteLine($"default {x.Current.FriendlyName}"); })
                .Select(change => change.Current)
                .Do(x => { Debug.WriteLine($"single default {x.FriendlyName}"); }))
            .Switch()
            .Select(CreateItem);
    }
}
