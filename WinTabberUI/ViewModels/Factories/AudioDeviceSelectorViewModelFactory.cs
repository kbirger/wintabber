using DynamicData;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Text;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Services;
using WinTabber.Api.Media.Repositories;

namespace WinTabberUI.ViewModels.Factories;

public class AudioDeviceSelectorViewModelFactory(
    AudioDeviceService deviceService
)
{
    private readonly AudioDeviceService _deviceService = deviceService;

    public AudioDeviceSelectorViewModel Create(DataFlow flow)
    {       
        return new AudioDeviceSelectorViewModel(_deviceService, flow);
    }
}
