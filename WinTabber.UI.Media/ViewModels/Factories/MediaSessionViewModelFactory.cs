using WinTabber.Api.Media.CoreAudio.Services;
using WinTabber.UI.Media.Models;
using WinTabber.UI.Media.ViewModels;

namespace WinTabber.UI.Media.ViewModels.Factories;

public class MediaSessionViewModelFactory(
    AudioSessionService audioSessionService,
    AudioDeviceService audioDeviceService)
{
    private readonly AudioSessionService _audioSessionService = audioSessionService;
    private readonly AudioDeviceService _audioDeviceService = audioDeviceService;

    public MediaSessionViewModel Create(AggregateSession session)
    {
        return new MediaSessionViewModel(session, _audioSessionService, _audioDeviceService);
    }
}
