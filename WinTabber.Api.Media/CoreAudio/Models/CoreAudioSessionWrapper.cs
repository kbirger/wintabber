using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.Api.Media.CoreAudio.Models;

public class CoreAudioSessionWrapper :  IAudioSessionEventsHandler, IDisposable
{
    private readonly AudioSessionControl _nativeSession;

    public uint ProcessId { get; }
    public string Id { get; }

    private bool _disposed = false;

    private Subject<Unit> _sessionEnded = new Subject<Unit>();
    private Subject<Unit> _changes = new Subject<Unit>();
    private Subject<string> _displayName = new();
    private Subject<AudioSessionState> _state = new();
    private Subject<(bool IsMuted, float Volume)> _volumeChanges = new();
    public IObservable<Unit> SessionEnded => _sessionEnded;
    public IObservable<Unit> SessionChanged => _changes;

    public IObservable<(bool IsMuted, float Volume)> VolumeChanges => _volumeChanges;

    public IObservable<string> DisplayName => _displayName;

    public IObservable<AudioSessionState> StateChanges => _state;
    public CoreAudioSessionWrapper(AudioSessionControl nativeSession)
    {
        _nativeSession = nativeSession;
        _state.OnNext(nativeSession.State);
        _volumeChanges.OnNext((nativeSession.SimpleAudioVolume.Mute, nativeSession.SimpleAudioVolume.Volume));
        _displayName.OnNext(nativeSession.DisplayName);
        ProcessId = _nativeSession.GetProcessID;
        Id = _nativeSession.GetSessionIdentifier;
        _nativeSession.RegisterEventClient(this);
    }

    public void Dispose()
    {
        try
        {
            _nativeSession.UnRegisterEventClient(this);
            _nativeSession.Dispose();
            _disposed = true;
        }
        catch
        {
            // Ignore exceptions during dispose
        }
    }

    ~CoreAudioSessionWrapper()
    {
        if (!_disposed)
        {
            Dispose();
        }
    }


    public AudioSessionControl NativeSession => _nativeSession;

    

    public void OnChannelVolumeChanged(uint channelCount, nint newVolumes, uint channelIndex)
    {
        Debug.WriteLine("Channel volume changed");
        //OnPropertyChanged(nameof(Volume));
    }

    public void OnDisplayNameChanged(string displayName)
    {
        Debug.WriteLine("DisplayName changed");
        _displayName.OnNext(displayName);
        _changes.OnNext(Unit.Default);

        //OnPropertyChanged(nameof(DisplayName));
    }

    public void OnGroupingParamChanged(ref Guid groupingId)
    {
        
    }

    public void OnIconPathChanged(string iconPath)
    {
        _changes.OnNext(Unit.Default);
        Debug.WriteLine("Icon path changed");

        //OnPropertyChanged(nameof(IconPath));
    }

    public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
    {
        Debug.WriteLine("Session disconnected");
        _changes.OnNext(Unit.Default);
        _sessionEnded.OnNext(Unit.Default);

    }

    public void OnStateChanged(AudioSessionState state)
    {
        Debug.WriteLine($"Session state changed: {NativeSession.GetSessionIdentifier} {state}");
        _state.OnNext(state);
        _changes.OnNext(Unit.Default);
        if(state == AudioSessionState.AudioSessionStateExpired)
        {
            _sessionEnded.OnNext(Unit.Default);
        }

    }

    public void OnVolumeChanged(float volume, bool isMuted)
    {
        Debug.WriteLine($"volume changed");
        _volumeChanges.OnNext((isMuted, volume));
        //OnPropertyChanged(nameof(Volume));
    }
}
