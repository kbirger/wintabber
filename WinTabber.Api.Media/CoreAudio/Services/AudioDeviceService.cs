using Accessibility;
using DynamicData;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Text;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Models;
using WinTabber.Api.Media.CoreAudio.Repositories;
using WinTabber.Api.Media.Repositories;

namespace WinTabber.Api.Media.CoreAudio.Services;

public class AudioDeviceService(CoreAudioDeviceRepository repository)
{
    private readonly CoreAudioDeviceRepository _repository = repository;

    private IObservableCache<MMDevice, string> _nativeDevices = repository.Devices.AsObservableCache();
    private IObservableCache<DefaultDeviceChange, DefaultDeviceKey> _defaultDevices = repository.GetDefaultDevices().AsObservableCache();

    public ObservableDeviceDto WatchDevice(string deviceId)
    {
        var deviceLookup = _nativeDevices.Lookup(deviceId);

        if(deviceLookup.HasValue)
        {
            var device = deviceLookup.Value;
            var deviceEvents = _repository.Watch(device);
            return new ObservableDeviceDto
            {

                DisplayName = device.DeviceFriendlyName ?? device.FriendlyName,
                Id = device.ID,
                IsDefaultChanges = deviceEvents.IsDefaultChanges,
                PropertyChanges = deviceEvents.PropertyChanges,
                Removed = deviceEvents.Removed,
                StateChanges = deviceEvents.StateChanges,
                VolumeChanges = deviceEvents.VolumeChanges.Select(change => change.MasterVolume),
                MuteChanges = deviceEvents.VolumeChanges.Select(change => change.Muted)
            };
        }

        throw new InvalidOperationException("No such device");
    }
    public IObservable<IChangeSet<DeviceDto, string>> GetDevices()
    {
        
        return _nativeDevices
            .Connect()
            .Transform(data =>
            {
                var isDefault = _defaultDevices.Lookup(new DefaultDeviceKey(data.DataFlow, Role.Multimedia));
                
                return new DeviceDto
                {
                    DeviceId = data.ID,
                    IsSelected = isDefault.HasValue && isDefault.Value.DeviceId == data.ID,
                    DeviceFriendlyName = data.DeviceFriendlyName,
                    DeviceName = data.FriendlyName
                };
            })
            .Publish()
            .RefCount();
    }
}
