using System;
using System.Collections.Generic;
using System.Text;
using WinTabber.Api.Media.CoreAudio.Services;
using WinTabberUI.Models;

namespace WinTabberUI.ViewModels.Factories;

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
