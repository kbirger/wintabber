using NAudio.CoreAudioApi.Interfaces;
using System.Reactive.Linq;
using WinTabber.Api.Media.CoreAudio.Models;

namespace WinTabber.Api.Media.CoreAudio.Dtos;

public class ObservableSessionDto
{
    public ObservableSessionDto(CoreAudioSessionWrapper session)
    {
        IsMutedChanges = session.VolumeChanges.Select(change => change.IsMuted).DistinctUntilChanged();
        VolumeChanges = session.VolumeChanges.Select(change => change.Volume).DistinctUntilChanged();
        State = session.StateChanges;
        DisplayName = session.DisplayName;

    }

    public IObservable<bool> IsMutedChanges { get; }
    public IObservable<float> VolumeChanges { get; }
    public IObservable<AudioSessionState> State { get; }
    public IObservable<string> DisplayName { get; }
}
