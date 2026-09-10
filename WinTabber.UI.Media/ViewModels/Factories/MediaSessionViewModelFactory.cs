using WinTabber.Api.Media.CoreAudio.Services;
using WinTabber.UI.Media.Models;
using WinTabber.UI.Media.ViewModels;

namespace WinTabber.UI.Media.ViewModels.Factories;

public class MediaSessionViewModelFactory(
    IAudioSessionService audioSessionService,
    IAudioDeviceService audioDeviceService)
{
    private readonly IAudioSessionService _audioSessionService = audioSessionService;
    private readonly IAudioDeviceService _audioDeviceService = audioDeviceService;

    public MediaSessionViewModel Create()
    {
        return new MediaSessionViewModel(_audioSessionService, _audioDeviceService);
    }
}
