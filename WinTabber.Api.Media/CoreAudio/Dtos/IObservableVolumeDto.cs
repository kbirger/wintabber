using System;
using System.Collections.Generic;
using System.Reactive;
using System.Text;

namespace WinTabber.Api.Media.CoreAudio.Dtos;

public interface IObservableVolumeDto
{
    public IObservable<bool> IsMutedChanges { get; }
    public IObservable<float> VolumeChanges { get; }

    public IObservable<bool> CanMuteChanges { get; }
    public IObservable<bool> CanSetVolumeChanges { get; }
    public Func<float, IObservable<Unit>> SetVolume { get; }
    public Func<bool, IObservable<Unit>> SetMute { get; }

}
