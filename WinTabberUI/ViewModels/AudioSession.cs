using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using WinTabberUI.Infrastructure;

namespace WinTabberUI.ViewModels;

public partial class AudioSession : ReactiveObject, IAudioSessionEventsHandler, IDisposable
{

    public string AumId { get; }
    public  string Name { get; }
    private int ProcessId { get; }

    private readonly AudioSessionControl _innerSession;

    public Process Process { get; private set; }

    private Action<AudioSession> _onDispose;
    public string? ProcessFilePath { get; }

    private CompositeDisposable _cleanup = new CompositeDisposable();

    private ReplaySubject<float> _volumeSubject = new(1);
    private ReplaySubject<bool> _isMutedSubject = new(1);
    private ReplaySubject<string> _displayNameSubject = new(1);
    private ReplaySubject<string> _iconPathSubject = new(1);
    //private ReplaySubject<(uint ChannelCount, nint NewVolumes, uint ChannelIndex)> _channelVolumesubject = new ();
    private ReplaySubject<AudioSessionState> _stateSubject = new(1);
    private ReplaySubject<AudioSessionDisconnectReason> _disconnectsSubject = new(1);

    private ObservableAsPropertyHelper<float> _volume;
    private ObservableAsPropertyHelper<bool> _isMuted;
    private ObservableAsPropertyHelper<string> _displayName;
    private ObservableAsPropertyHelper<string> _iconPath;
    //private ObservableAsPropertyHelper<(uint Chann;
    private ObservableAsPropertyHelper<AudioSessionState> _state;
    //private ObservableAsPropertyHelper<AudioSessionDisconnectReason> _disconnects;
    private Subject<Unit> _disposed = new Subject<Unit>();
    public IObservable<Unit> OnDisposed => _disposed;

    public AudioSession(AudioSessionControl innerSession, Action<AudioSession> onDispose)
    {
        Name = innerSession.GetSessionInstanceIdentifier;
        ProcessId = Convert.ToInt32(innerSession.GetProcessID);
        _innerSession = innerSession;
        Process = Process.GetProcessById(ProcessId);
        ProcessFilePath = Process.MainModule?.FileName;
        
        var aumid = Process?.TryGetAumid();
        _onDispose = onDispose;
        _state = _stateSubject
            .DistinctUntilChanged()
            .ToProperty(this, x => x.State);

        if (aumid is null)
        {
            State = AudioSessionState.AudioSessionStateExpired;
            return;
        }
        AumId = aumid;
        _innerSession.RegisterEventClient(this);

        _volume = _volumeSubject
            .DistinctUntilChanged()
            .ToProperty(this, x => x.Volume)
            .DisposeWith(_cleanup);

        _isMuted = _isMutedSubject
            .DistinctUntilChanged()
            .ToProperty(this, x => x.IsMuted);

        _displayName = _displayNameSubject
            .DistinctUntilChanged()
            .ToProperty(this, x => x.DisplayName);

        

        this.WhenAnyValue(vm => vm.State)
            .Where(state => state != AudioSessionState.AudioSessionStateActive)
            .Take(1)
            .Subscribe(_ => Dispose());

    }

    public float Volume
    {
        get => _volume.Value;
        set => _volumeSubject.OnNext(value);
    }

    public bool IsMuted
    {
        get => _isMuted.Value;
        set => _isMutedSubject.OnNext(value);
    }

    public string DisplayName
    {
        get => _displayName.Value;
        set => _displayNameSubject.OnNext(value);
    }

    public AudioSessionState State
    {
        get => _state.Value;
        private set => _stateSubject.OnNext(value);
    }


    public void OnVolumeChanged(float volume, bool isMuted)
    {
        Volume = volume;
        IsMuted = isMuted;
    }

    public void OnDisplayNameChanged(string displayName)
    {
        DisplayName = displayName; 
    }

    public void OnIconPathChanged(string iconPath)
    {
        
    }

    public void OnChannelVolumeChanged(uint channelCount, nint newVolumes, uint channelIndex)
    {
        
    }

    public void OnGroupingParamChanged(ref Guid groupingId)
    {
        
    }

    public void OnStateChanged(AudioSessionState state)
    {
        State = state;
    }

    public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
    {
        State = AudioSessionState.AudioSessionStateExpired;
    }

    public void Dispose()
    {
        _innerSession.UnRegisterEventClient(this);
        _cleanup.Dispose();
        _disposed.OnNext(Unit.Default);
    }
}
