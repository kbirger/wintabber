using CoreAudio.Interfaces;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace WinTabberUI.ViewModels;

public partial class AudioSession : IAudioSessionEventsHandler, IDisposable
{

    private string AumId { get; }
    private string Name { get; }
    private int ProcessId { get; }

    private readonly AudioSessionControl _innerSession;
    private Action<AudioSession> _onDispose;
    public string? ProcessFilePath { get; }

    private CompositeDisposable _cleanup = new CompositeDisposable();

    private ReplaySubject<float> _volumeSubject = new (1);
    private ReplaySubject<bool> _isMutedSubject = new (1);
    private ReplaySubject<string> _displayNameSubject = new (1);
    private ReplaySubject<string> _iconPathSubject = new (1);
    //private ReplaySubject<(uint ChannelCount, nint NewVolumes, uint ChannelIndex)> _channelVolumesubject = new ();
    private ReplaySubject<AudioSessionState> _statSubjecte= new (1);
    private ReplaySubject<AudioSessionDisconnectReason> _disconnectsSubject = new (1);


    public AudioSession(string aumId, string name, int processId, AudioSessionControl innerSession, Action<AudioSession> onDispose)
    {
        AumId = aumId;
        Name = name;
        ProcessId = processId;
        _innerSession = innerSession;
        ProcessFilePath = Process.GetProcessById(Convert.ToInt32(ProcessId)).MainModule?.FileName;
        _onDispose = onDispose;

        _innerSession.RegisterEventClient(this);

        _volume = VolumeChanges
            .Merge(_volume)
            .DistinctUntilChanged()
            .Select()
            .ToProperty(this, x => x.Volume, )
            .DisposeWith(_cleanup);



    }

    public float Volume
    {
        get =>_volume.va;
    }



    public void OnVolumeChanged(float volume, bool isMuted)
    {
        _volume.OnNext(volume);
        _isMuted.OnNext(isMuted);

    }

    public void OnDisplayNameChanged(string displayName)
    {
        throw new NotImplementedException();
    }

    public void OnIconPathChanged(string iconPath)
    {
        throw new NotImplementedException();
    }

    public void OnChannelVolumeChanged(uint channelCount, nint newVolumes, uint channelIndex)
    {
        throw new NotImplementedException();
    }

    public void OnGroupingParamChanged(ref Guid groupingId)
    {
        throw new NotImplementedException();
    }

    public void OnStateChanged(AudioSessionState state)
    {
        throw new NotImplementedException();
    }

    public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}
