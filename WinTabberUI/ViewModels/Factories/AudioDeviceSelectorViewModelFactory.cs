using NAudio.CoreAudioApi;
using WinTabber.Api.Media.CoreAudio.Services;

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
