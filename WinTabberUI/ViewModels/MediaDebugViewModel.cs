using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using NAudio.CoreAudioApi;
using ReactiveUI;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Models;
using WinTabber.Api.Media.CoreAudio.Repositories;
using WinTabber.Api.Media.CoreAudio.Services;
using WinTabber.Api.Media.ShellApplications.Models;
using WinTabber.Api.Media.ShellApplications.Repositories;
using WinTabber.Api.Media.SMTC.Repositories;
using WinTabber.UI.Media.Models;
using WinTabber.UI.Media.Services;

namespace WinTabberUI.ViewModels;

/// <summary>
/// Live view of the six caches behind the media controls feature. It reads the same services that
/// the feature reads, so what it shows is what the feature sees.
/// </summary>
/// <remarks>
/// Attach and Detach are driven by MediaDebugWindowCoordinator. Do not subscribe in the
/// constructor. Several sources are ref-counted, so a subscription that outlives the media controls
/// window keeps the ref count above zero and stops the teardown that this tool exists to show.
/// </remarks>
public class MediaDebugViewModel : ReactiveObject
{
    private readonly AudioDeviceService _deviceService;
    private readonly AudioSessionService _sessionService;
    private readonly SMTCSessionRepository _smtcRepository;
    private readonly InstalledApplicationRepository _appRepository;
    private readonly MediaSessionService _mediaSessionService;
    private readonly IScheduler _comScheduler;

    private CompositeDisposable? _cleanUp;

    private ReadOnlyObservableCollection<DeviceRow> _playbackDevices = new([]);
    private ReadOnlyObservableCollection<DeviceRow> _recordingDevices = new([]);
    private ReadOnlyObservableCollection<SmtcSessionRow> _smtcSessions = new([]);
    private ReadOnlyObservableCollection<CoreAudioSessionRow> _coreAudioSessions = new([]);
    private ReadOnlyObservableCollection<InstalledAppRow> _installedApps = new([]);
    private ReadOnlyObservableCollection<MasterSessionRow> _masterSessions = new([]);
    private string _status = "Detached.";

    public MediaDebugViewModel(
        AudioDeviceService deviceService,
        AudioSessionService sessionService,
        SMTCSessionRepository smtcRepository,
        InstalledApplicationRepository appRepository,
        MediaSessionService mediaSessionService,
        [FromKeyedServices(STAScheduler.Key)] IScheduler comScheduler
    )
    {
        _deviceService = deviceService;
        _sessionService = sessionService;
        _smtcRepository = smtcRepository;
        _appRepository = appRepository;
        _mediaSessionService = mediaSessionService;
        _comScheduler = comScheduler;
    }

    public ReadOnlyObservableCollection<DeviceRow> PlaybackDevices
    {
        get => _playbackDevices;
        private set => this.RaiseAndSetIfChanged(ref _playbackDevices, value);
    }

    public ReadOnlyObservableCollection<DeviceRow> RecordingDevices
    {
        get => _recordingDevices;
        private set => this.RaiseAndSetIfChanged(ref _recordingDevices, value);
    }

    public ReadOnlyObservableCollection<SmtcSessionRow> SmtcSessions
    {
        get => _smtcSessions;
        private set => this.RaiseAndSetIfChanged(ref _smtcSessions, value);
    }

    public ReadOnlyObservableCollection<CoreAudioSessionRow> CoreAudioSessions
    {
        get => _coreAudioSessions;
        private set => this.RaiseAndSetIfChanged(ref _coreAudioSessions, value);
    }

    public ReadOnlyObservableCollection<InstalledAppRow> InstalledApps
    {
        get => _installedApps;
        private set => this.RaiseAndSetIfChanged(ref _installedApps, value);
    }

    public ReadOnlyObservableCollection<MasterSessionRow> MasterSessions
    {
        get => _masterSessions;
        private set => this.RaiseAndSetIfChanged(ref _masterSessions, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    /// <summary>
    /// Subscribes to every source. The caller must call this when the media controls window opens.
    /// </summary>
    public void Attach()
    {
        if (_cleanUp is not null)
        {
            return;
        }

        var cleanUp = new CompositeDisposable();

        // Same call the real selector makes: AudioDeviceSelectorViewModel filters one device cache
        // by data flow.
        var devices = _deviceService.Devices.Connect();
        PlaybackDevices = BindDevices(devices, DataFlow.Render, cleanUp);
        RecordingDevices = BindDevices(devices, DataFlow.Capture, cleanUp);

        _smtcRepository
            .MediaSessions.Transform(session => new SmtcSessionRow(session, _comScheduler))
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Bind(out var smtcSessions)
            .DisposeMany()
            .Subscribe()
            .DisposeWith(cleanUp);
        SmtcSessions = smtcSessions;

        _sessionService
            .CoreAudioSessions.Connect()
            .Transform(session => new CoreAudioSessionRow(session))
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Bind(out var coreAudioSessions)
            .DisposeMany()
            .Subscribe()
            .DisposeWith(cleanUp);
        CoreAudioSessions = coreAudioSessions;

        _appRepository
            .ApplicationsByAumid.Connect()
            .Transform(CreateAppRow)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Bind(out var installedApps)
            .Subscribe()
            .DisposeWith(cleanUp);
        InstalledApps = installedApps;

        // transformOnRefresh keeps the native-session column truthful: the aggregate cache reports a
        // late native-session match as a refresh, not as an add.
        _mediaSessionService
            .MasterSessions.Connect()
            .Transform(CreateMasterRow, true)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Bind(out var masterSessions)
            .Subscribe()
            .DisposeWith(cleanUp);
        MasterSessions = masterSessions;

        _cleanUp = cleanUp;
        Status = $"Attached at {DateTime.Now:HH:mm:ss}.";
    }

    /// <summary>
    /// Releases every subscription. The lists keep their last values so that they stay readable.
    /// </summary>
    public void Detach()
    {
        if (_cleanUp is null)
        {
            return;
        }

        _cleanUp.Dispose();
        _cleanUp = null;
        Status = $"Detached at {DateTime.Now:HH:mm:ss}. The lists show the last known state.";
    }

    private static ReadOnlyObservableCollection<DeviceRow> BindDevices(
        IObservable<IChangeSet<DeviceDto, string>> devices,
        DataFlow flow,
        CompositeDisposable cleanUp
    )
    {
        devices
            .Filter(device => device.DataFlow == flow)
            .Transform(CreateDeviceRow)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Bind(out var rows)
            .Subscribe()
            .DisposeWith(cleanUp);

        return rows;
    }

    private static DeviceRow CreateDeviceRow(DeviceDto device)
    {
        return new DeviceRow(device.DeviceId, device.DeviceName, device.DeviceFriendlyName, device.DataFlow.ToString());
    }

    private static InstalledAppRow CreateAppRow(InstalledApplicationInfo app)
    {
        return new InstalledAppRow(
            app.AppUserModelId,
            app.Name,
            app.TargetPath ?? string.Empty,
            app.PackageInstallPath ?? string.Empty
        );
    }

    private static MasterSessionRow CreateMasterRow(AggregateSession session)
    {
        return new MasterSessionRow(
            session.Key.ToString() ?? string.Empty,
            session.App.Name,
            session.App.AppUserModelId,
            session.NativeSession?.Id ?? "none"
        );
    }
}
