using DynamicData;
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Text;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.Repositories;

namespace WinTabber.Api.Media.CoreAudio.Services;

public class AudioDeviceService(CoreAudioDeviceRepository repository)
{
    private readonly CoreAudioDeviceRepository _repository = repository;

    public IObservable<IChangeSet<DeviceDto, string>> GetDevices()
    {
        return _repository.Devices
            .Transform(device => new DeviceDto
            {
                DeviceId = device.ID,
                DeviceFriendlyName = device.DeviceFriendlyName,
                DeviceName = device.FriendlyName
            })
            .Publish()
            .RefCount();
    }
}
