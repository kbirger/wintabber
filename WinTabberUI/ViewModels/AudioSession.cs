using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using WinTabberUI.Infrastructure;

namespace WinTabberUI.ViewModels;

public partial class AudioSession : ReactiveObject, IAudioSessionEventsHandler, IDisposable
{

    public string AumId { get; }

    public string Name { get; }
    private int ProcessId { get; }

    private readonly AudioSessionControl _innerSession;

    public Process Process { get; private set; }

    private Action<AudioSession> _onDispose;
    public string? ProcessFilePath { get; }

    private CompositeDisposable _cleanup = new CompositeDisposable();

    private readonly ReplaySubject<string> _displayNameSubject = new(1);
    private readonly ReplaySubject<string> _iconPathSubject = new(1);
    //private ReplaySubject<(uint ChannelCount, nint NewVolumes, uint ChannelIndex)> _channelVolumesubject = new ();
    private ReplaySubject<AudioSessionState> _stateSubject = new(1);
    private ReplaySubject<AudioSessionDisconnectReason> _disconnectsSubject = new(1);

    private readonly ObservableAsPropertyHelper<string> _displayName;
    private readonly ObservableAsPropertyHelper<string> _iconPath;
    //private ObservableAsPropertyHelper<(uint Chann;
    private readonly ObservableAsPropertyHelper<AudioSessionState> _state;
    //private ObservableAsPropertyHelper<AudioSessionDisconnectReason> _disconnects;
    private readonly Subject<Unit> _disposed = new Subject<Unit>();
    public IObservable<Unit> OnDisposed => _disposed;


    public static AudioSession? Create(AppCache cache, AudioSessionControl nativeSession)
    {
        var process = Process.GetProcessById(Convert.ToInt32(nativeSession.GetProcessID));

        string? aumid = null;

        while (process is not null && aumid is null)
        {
            try
            {
                aumid = cache.GetByPath(process.MainModule?.FileName)?.AppUserModelId;
                if (aumid is null)
                {
                    process = GetParentProcess(process);
                }
            }
            catch
            {
                // process is not accessible
            }
        }

        if (aumid is null)
        {
            return null;
        }

        var session = new AudioSession(aumid, nativeSession, (_) => { });
        if (session.State != AudioSessionState.AudioSessionStateExpired)
        {
            return session;
        }

        return null;
    }
    private static Process? GetParentProcess(Process process)
    {
        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = " + process.Id))
            {
                ManagementObjectCollection processes = searcher.Get();
                foreach (ManagementObject obj in processes)
                {
                    uint parentProcessId = (uint)obj["ParentProcessId"];
                    return Process.GetProcessById((int)parentProcessId);
                }
            }
        }
        catch
        {
            // Handle exceptions (e.g., parent process terminated, insufficient permissions)
        }
        return null;
    }

    public static AudioSession? Create(AppCache cache, IAudioSessionControl nativeSession)
    {
        return Create(cache, new AudioSessionControl(nativeSession));
    }


    public AudioSession(string? aumid, AudioSessionControl innerSession, Action<AudioSession> onDispose)
    {
        try
        {

            Name = innerSession.GetSessionInstanceIdentifier;
            ProcessId = Convert.ToInt32(innerSession.GetProcessID);
            _innerSession = innerSession;
            Process = Process.GetProcessById(ProcessId);
            ProcessFilePath = Process.MainModule?.FileName;

            _onDispose = onDispose;
            _state = _stateSubject
                .DistinctUntilChanged()
                .ToProperty(this, x => x.State);
        }
        catch (COMException)
        {
            State = AudioSessionState.AudioSessionStateExpired;
            return;
        }

        if (aumid is null)
        {
            State = AudioSessionState.AudioSessionStateExpired;
            return;
        }
        AumId = aumid;
        _innerSession.RegisterEventClient(this);




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
        get => _innerSession.SimpleAudioVolume.Volume;
        set
        {
            _innerSession.SimpleAudioVolume.Volume = value;
            this.RaisePropertyChanged();
        }
    }

    public bool IsMuted
    {
        get => _innerSession.SimpleAudioVolume.Mute;
        set
        {
            _innerSession.SimpleAudioVolume.Mute = value;
            this.RaisePropertyChanged();
        }
    }

    public string DisplayName
    {
        get => _displayName?.Value ?? "";
        set => _displayNameSubject.OnNext(value);
    }

    public AudioSessionState State
    {
        get => _state.Value;
        private set => _stateSubject.OnNext(value);
    }


    public void OnVolumeChanged(float volume, bool isMuted)
    {
        this.RaisePropertyChanged(nameof(Volume));
        this.RaisePropertyChanged(nameof(IsMuted));
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
