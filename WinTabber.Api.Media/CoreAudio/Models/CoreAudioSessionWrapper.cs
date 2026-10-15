using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using WinTabber.Api.Media.CoreAudio;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

namespace WinTabber.Api.Media.CoreAudio.Models;

public class CoreAudioSessionWrapper :  IAudioSessionEventsHandler, IDisposable
{
    private readonly AudioSessionControl _coreAudioSession;
    private readonly IScheduler _scheduler;


    public IAudioDevice Device { get; }
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

    // Latest display name, kept current by OnDisplayNameChanged. Reading it makes no COM call.
    public string CurrentDisplayName => _displayName.Value;

    public IObservable<AudioSessionState> StateChanges => _stateChanges;
    public CoreAudioSessionWrapper(AudioSessionControl nativeSession, IAudioDevice device, IScheduler scheduler)
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


    // The live NAudio session is private on purpose: only code that runs on the CoreAudioWorker
    // thread and handles a dead device may touch it. Everything else reads the cached values above.

    public void OnChannelVolumeChanged(uint channelCount, nint newVolumes, uint channelIndex)
    {
        Debug.WriteLine("Channel volume changed");
        //OnPropertyChanged(nameof(Volume));
    }

    // The audio service calls the IAudioSessionEvents callbacks on its own thread, not on the
    // CoreAudioWorker thread. An exception that leaves a callback ends the process, and a COM call
    // fails with COMException (for example 0x88890004, device invalidated) when the device is
    // removed or reset. Each callback therefore runs inside this guard.
    private static void Guard(Action callback)
    {
        try
        {
            callback();
        }
        catch (COMException ex)
        {
            Debug.WriteLine($"Ignored COM failure in audio session callback: {ex.Message}");
        }
    }

    public void OnDisplayNameChanged(string displayName) =>
        Guard(() =>
        {
            Debug.WriteLine("DisplayName changed");
            _displayName.OnNext(displayName);
            _changes.OnNext(Unit.Default);

            //OnPropertyChanged(nameof(DisplayName));
        });

    public void OnGroupingParamChanged(ref Guid groupingId)
    {

    }

    public void OnIconPathChanged(string iconPath) =>
        Guard(() =>
        {
            _changes.OnNext(Unit.Default);
            Debug.WriteLine("Icon path changed");

            //OnPropertyChanged(nameof(IconPath));
        });

    public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) =>
        Guard(() =>
        {
            Debug.WriteLine("Session disconnected");
            _changes.OnNext(Unit.Default);
            _sessionEnded.OnNext(Unit.Default);
        });

    public void OnStateChanged(AudioSessionState state) =>
        Guard(() =>
        {
            Debug.WriteLine($"Session state changed: {Id} {state}");
            _stateChanges.OnNext(state);
            _changes.OnNext(Unit.Default);
            if (state == AudioSessionState.AudioSessionStateExpired)
            {
                _sessionEnded.OnNext(Unit.Default);
            }
        });

    public void OnVolumeChanged(float volume, bool isMuted) =>
        Guard(() =>
        {
            Debug.WriteLine($"[{this.Id}]volume changed: {volume}");
            _volumeChanges.OnNext((isMuted, volume));
            //OnPropertyChanged(nameof(Volume));
        });

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
