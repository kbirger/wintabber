using NAudio.CoreAudioApi;
using WinTabber.Api.Media.CoreAudio.Services;
using WinTabber.UI.Media.ViewModels;

namespace WinTabber.UI.Media.ViewModels.Factories;

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
