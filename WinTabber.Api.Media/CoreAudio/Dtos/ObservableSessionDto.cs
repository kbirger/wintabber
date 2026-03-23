using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Text;
using WinTabber.Api.Media.CoreAudio.Models;

namespace WinTabber.Api.Media.CoreAudio.Dtos;

public class ObservableSessionDto
{
    public ObservableSessionDto(CoreAudioSessionWrapper deviceWrapper)
    {
        IsMutedChanges = deviceWrapper.VolumeChanges.Select(change => change.IsMuted).DistinctUntilChanged();
        VolumeChanges = deviceWrapper.VolumeChanges.Select(change => change.Volume).DistinctUntilChanged();
        State = deviceWrapper.StateChanges;
        DisplayName = deviceWrapper.DisplayName;

    }

    public IObservable<bool> IsMutedChanges { get; }
    public IObservable<float> VolumeChanges { get; }
    public IObservable<AudioSessionState> State { get; }
    public IObservable<string> DisplayName { get; }
}
