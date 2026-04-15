using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

namespace WinTabber.Api.Media.CoreAudio.Models;

public class CoreAudioSessionWrapper :  IAudioSessionEventsHandler, IDisposable
{
    private readonly AudioSessionControl _coreAudioSession;
    private readonly IScheduler _scheduler;


    public CoreAudioDeviceWrapper Device { get; }
    public uint ProcessId { get; }
    public string Id { get; }

    private bool _disposed = false;

    private Subject<Unit> _sessionEnded = new Subject<Unit>();
    private Subject<Unit> _changes = new Subject<Unit>();
    private BehaviorSubject<string> _displayName;
    private BehaviorSubject<AudioSessionState> _stateChanges;
    private BehaviorSubject<(bool IsMuted, float Volume)> _volumeChanges;
    public IObservable<Unit> SessionEnded => _sessionEnded;
    public IObservable<Unit> SessionChanged => _changes;

    public IObservable<(bool IsMuted, float Volume)> VolumeChanges => _volumeChanges;

    public IObservable<string> DisplayName => _displayName;

    public IObservable<AudioSessionState> StateChanges => _stateChanges;
    public CoreAudioSessionWrapper(AudioSessionControl nativeSession, CoreAudioDeviceWrapper device, IScheduler scheduler)
    {
        // Set internal Device property so that code inside this project can access directly
        // as it will already be running on correct thread
        Device = device;

        _coreAudioSession = nativeSession;
        _scheduler = scheduler;
        _stateChanges = new(nativeSession.State);
        _volumeChanges = new((nativeSession.SimpleAudioVolume.Mute, nativeSession.SimpleAudioVolume.Volume));
        _displayName = new (nativeSession.DisplayName);
        ProcessId = _coreAudioSession.GetProcessID;
        Id = _coreAudioSession.GetSessionIdentifier;
        _coreAudioSession.RegisterEventClient(this);

    }

    public void Dispose()
    {
        try
        {
            _coreAudioSession.UnRegisterEventClient(this);
            _coreAudioSession.Dispose();
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


    internal AudioSessionControl CoreAudioSession => _coreAudioSession;

    

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
        Debug.WriteLine($"Session state changed: {CoreAudioSession.GetSessionIdentifier} {state}");
        _stateChanges.OnNext(state);
        _changes.OnNext(Unit.Default);
        if(state == AudioSessionState.AudioSessionStateExpired)
        {
            _sessionEnded.OnNext(Unit.Default);
        }

    }

    public void OnVolumeChanged(float volume, bool isMuted)
    {
        Debug.WriteLine($"[{this.Id}]volume changed: {volume}");
        _volumeChanges.OnNext((isMuted, volume));
        //OnPropertyChanged(nameof(Volume));
    }

    internal IObservable<Unit> SetVolume(float volume)
    {
        return Observable.Start(() =>
        {
            if(Math.Abs(_coreAudioSession.SimpleAudioVolume.Volume - volume) > .01)
            {
                _coreAudioSession.SimpleAudioVolume.Volume = volume;
            }
        }, _scheduler);
    }

    internal IObservable<Unit> SetMute(bool isMuted)
    {
        return Observable.Start(() =>
        {
            if (isMuted != _coreAudioSession.SimpleAudioVolume.Mute)
            {
                _coreAudioSession.SimpleAudioVolume.Mute = isMuted;
            }
        }, _scheduler);
    }
}
