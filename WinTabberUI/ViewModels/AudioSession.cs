using DynamicData;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using ReactiveUI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
using Windows.Wdk.System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using WinTabberUI.Infrastructure;
using WinTabberUI.Services;

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

    private static ConcurrentDictionary<string, string> memoized = new();

    public static AudioSession? Create(IObservableCache<InstalledApplicationInfo, string> installedApplicationsByPath, AudioSessionControl nativeSession)
    {
        Stopwatch sw = Stopwatch.StartNew();

        var sessionProcess = Process.GetProcessById(Convert.ToInt32(nativeSession.GetProcessID));
        var process = sessionProcess;
        var processName = process.ProcessName;


        string? aumid = null;

        // recursively try to get an aumid, starting with the process that owns the session, and travelling up the parent processes
        while (process is not null && aumid is null)
        {
            try
            {
                if (process.ProcessName == "svchost" || process.ProcessName == "explorer")
                {
                    break;
                }

                Stopwatch sw2 = Stopwatch.StartNew();

                if (TryGetProcessExecutablePath(process, out var processPath))
                {
                    var appOption = installedApplicationsByPath.Lookup(processPath);
                    if (appOption.HasValue)
                    {
                        aumid = appOption.Value.AppUserModelId;
                    }
                }

                sw2.Stop();
                //Debug.WriteLine($"Fetching process exe path took {sw.ElapsedMilliseconds}ms");

                // If not found, keep going
                if (aumid is null)
                {
                    process = GetParentProcess(process);
                }
            }
            catch
            {
                break;
                // process is not accessible
            }
        }
        sw.Stop();
        //Debug.WriteLine($"Got aumid '{aumid}' for session {nativeSession.DisplayName} - ({processName} - {sessionProcess.Id}) in {sw.ElapsedMilliseconds}ms");

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

    private static bool TryGetProcessExecutablePath(Process process, [MaybeNullWhen(false)] out string executablePath)
    {
        using var hProcess = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)process.Id);

        if (hProcess.IsInvalid)
        {
            executablePath = null;
            return false;
        }

        uint size = 1024;
        Span<char> psz = new char[size].AsSpan();

        if (PInvoke.QueryFullProcessImageName(hProcess, 0, psz, ref size))
        {

            executablePath = psz.Slice(0, (int)size).ToString();
            if (size > 0 && !string.IsNullOrWhiteSpace(executablePath))
            {
                return true;
            }
        }
        executablePath = null;
        return false;
    }
    private static Process? GetParentProcess2(Process process)
    {
        if (process.ProcessName == "explorer")
        {
            return null;
        }

        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = " + process.Id))
            {
                ManagementObjectCollection processes = searcher.Get();
                foreach (ManagementObject obj in processes)
                {
                    uint parentProcessId = (uint)obj["ParentProcessId"];
                    sw.Stop();
                    //Debug.WriteLine($"GetParentProcess for {process.ProcessName} ({process.Id}) took {sw.ElapsedMilliseconds}ms");
                    return Process.GetProcessById((int)parentProcessId);
                }
            }
        }
        catch
        {
            //Debug.WriteLine($"Failed ot get parent of process {process.ProcessName} ({process.Id})");
            // Handle exceptions (e.g., parent process terminated, insufficient permissions)
        }
        sw.Stop();
        //Debug.WriteLine($"GetParentProcess for {process.ProcessName} ({process.Id}) took {sw.ElapsedMilliseconds}ms");
        return null;
    }

    private unsafe static Process? GetParentProcess(Process process)
    {
        Stopwatch sw = Stopwatch.StartNew();
        PROCESS_BASIC_INFORMATION pbi;
        uint length = 0;
        var result = Windows.Wdk.PInvoke.NtQueryInformationProcess(
            new HANDLE(process.Handle),
            PROCESSINFOCLASS.ProcessBasicInformation,
            &pbi,
            (uint)Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(),
            ref length);

        if (result.SeverityCode > NTSTATUS.Severity.Informational)
        {
            Marshal.ThrowExceptionForHR((int)result);
        }

        sw.Stop();
        //Debug.WriteLine($"GetParentProcess => {pbi.InheritedFromUniqueProcessId} for {process.ProcessName} ({process.Id}) took {sw.ElapsedMilliseconds}ms");
        if (pbi.InheritedFromUniqueProcessId != 0)
        {
            return Process.GetProcessById((int)pbi.InheritedFromUniqueProcessId);

        }
        return null;
    }


    public static AudioSession? Create(IObservableCache<InstalledApplicationInfo, string> installedApplicationsByPath, IAudioSessionControl nativeSession)
    {
        return Create(installedApplicationsByPath, new AudioSessionControl(nativeSession));
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
