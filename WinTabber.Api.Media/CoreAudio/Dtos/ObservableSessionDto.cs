using System.Reactive;
using System.Reactive.Linq;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using WinTabber.Api.Media.CoreAudio.Models;

namespace WinTabber.Api.Media.CoreAudio.Dtos;

public class ObservableSessionDto : IObservableVolumeDto
{
    public ObservableSessionDto() { }

    public ObservableSessionDto(CoreAudioSessionWrapper? session)
    {
        if (session is null)
        {
            VolumeChanges = Observable.Empty<float>();
            IsMutedChanges = Observable.Empty<bool>();
            CanMuteChanges = Observable.Return(false);
            CanSetVolumeChanges = Observable.Return(false);
            SetVolume = (volume) => Observable.Empty<Unit>();
            SetMute = (volume) => Observable.Empty<Unit>();
            State = Observable.Empty<AudioSessionState>();
            DisplayName = Observable.Empty<string>();
        }
        else
        {
            IsMutedChanges = session.VolumeChanges.Select(change => change.IsMuted).DistinctUntilChanged();
            VolumeChanges = session.VolumeChanges.Select(change => change.Volume).DistinctUntilChanged();
            State = session.StateChanges;
            DisplayName = session.DisplayName;
            SetVolume = session.SetVolume;
            SetMute = session.SetMute;
        }
    }

    public IObservable<bool> IsMutedChanges { get; }
    public IObservable<float> VolumeChanges { get; }
    public IObservable<AudioSessionState> State { get; }
    public IObservable<string> DisplayName { get; }

    public IObservable<bool> CanMuteChanges { get; } = Observable.Return(true);

    public IObservable<bool> CanSetVolumeChanges { get; } = Observable.Return(true);

    public Func<float, IObservable<Unit>> SetVolume { get; }
    public Func<bool, IObservable<Unit>> SetMute { get; }
}
