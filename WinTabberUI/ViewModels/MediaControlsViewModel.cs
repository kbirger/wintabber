using DynamicData;
using NAudio.CoreAudioApi;
using ReactiveUI;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows;
using Windows.Media.Control;
using WinTabber.Events;
using WinTabber.Interop;
using WinTabberUI.Infrastructure;
using WinTabberUI.Services;

namespace WinTabberUI.ViewModels;

public class MediaControlsViewModel : ReactiveObject, IActivatableViewModel
{
    //public class DeviceItem : ReactiveObject
    //{
    //    public DeviceItem(MMDevice device)
    //    {
    //        Name = device.DeviceInterfaceFriendlyName;
    //        Id = device.ID;
    //        _isSelected = device.Selected;
    //        _device = device;
    //    }
    //    public string Name { get; }

    //    public string Id { get; }

    //    private readonly ObservableAsPropertyHelper<bool> _isActive;
    //    private readonly MMDevice _device;
    //    private bool _isSelected;
    //    public bool IsActive
    //    {
    //        get => _isActive.Value;
    //    }

    //    //public bool IsSelected
    //    //{
    //    //    get => _isSelected;
    //    //    set
    //    //    {
    //    //        _device.Selected = value;
    //    //        this.RaiseAndSetIfChanged(ref _isSelected, value);
    //    //    }
    //    //}
    //}


    //private class GlobalSystemMediaTransportControlsSessionComparer : IComparer<GlobalSystemMediaTransportControlsSession>, IComparer
    //{
    //    public int Compare(GlobalSystemMediaTransportControlsSession? x, GlobalSystemMediaTransportControlsSession? y)
    //    {
    //        return x?.SourceAppUserModelId.CompareTo(y?.SourceAppUserModelId) ?? 0;
    //    }

    //    int IComparer.Compare(object? x, object? y)
    //    {
    //        return this.Compare(x as GlobalSystemMediaTransportControlsSession, y as GlobalSystemMediaTransportControlsSession);
    //    }
    //}


    private IReadOnlyList<GlobalSystemMediaTransportControlsSession> _sessionModels;
    private IReadOnlyList<SessionItem> _sessions;
    private SessionItem _activeSession;
    private readonly InstalledApplicationService _applicationService;
    private readonly IMediaControlsStateService _mediaControlsStateService;
    private readonly IAudioDeviceManager _audioDeviceManager;



    private AudioDeviceSelectorViewModel? _playback;
    private AudioDeviceSelectorViewModel? _recording;
    private ObservableAsPropertyHelper<MediaSessionViewModel?> _sessionData;

    public AudioDeviceSelectorViewModel? Playback
    {
        get => _playback;
        set => this.RaiseAndSetIfChanged(ref _playback, value);
    }
    public AudioDeviceSelectorViewModel? Recording
    {
        get => _recording;
        set => this.RaiseAndSetIfChanged(ref _recording, value);
    }

    public ViewModelActivator Activator { get; } = new ViewModelActivator();
    public ReactiveCommand<Unit, Unit> PlayPause { get; private set; }
    public ReactiveCommand<Unit, Unit> Next { get; private set; }
    public ReactiveCommand<Unit, Unit> Prev { get; private set; }
    public ReactiveCommand<Unit, Unit> Mute { get; private set; }





    public MediaControlsViewModel(InstalledApplicationService applicationService, IMediaControlsStateService mediaControlsStateService, IAudioDeviceManager audioDeviceManager, WinTabberEventManager eventManager)
    {
        _applicationService = applicationService;
        _mediaControlsStateService = mediaControlsStateService;
        _audioDeviceManager = audioDeviceManager;
        Sessions = [];
        var scheduler = RxApp.MainThreadScheduler;


        Debug.WriteLine("Created");
        this.WhenActivated((disposables) =>
        {
            Debug.WriteLine("Activated");

            Disposable.Create(() => HandleDeactivation())
                .DisposeWith(disposables);

            //var playbackDevices = 
            //    GetDevices(DataFlow.Render)
            //        .Select(x => new DeviceItem(x)).ToArray();
            //var recordingDevices = 
            //    GetDevices(DataFlow.Capture)
            //        .Select(x => new DeviceItem(x)).ToArray();

            //Observable.Return(playbackDevices).ToProperty(this, vm => vm.PlaybackDevices, out _playbackDevices ).ThrownExceptions.Subscribe(ex => { Debug.WriteLine(ex); }) ;
            //Observable.Return(recordingDevices).ToProperty(this, vm => vm.RecordingDevices, out _recordingDevices).ThrownExceptions.Subscribe(ex => { Debug.WriteLine(ex); });

            //var (playback, recording) = AudioDeviceSelectorViewModel.Create();
            var ad = _audioDeviceManager.Connect();
            var renderDevices = ad.Filter(x => x.Kind == DataFlow.Render);
            var recordingDevices = ad.Filter(x => x.Kind == DataFlow.Capture);
            var sessions = ad.TransformMany(device => new DeviceSessionWatcher(device.Device.AudioSessionManager, applicationService.ApplicationsByPath).Connect().AsObservableCache(), x => x.AumId).AsObservableCache();

            _playback = new AudioDeviceSelectorViewModel(renderDevices);
            _recording = new AudioDeviceSelectorViewModel(recordingDevices);

            //.Select(devices => new AudioDeviceSelectorViewModel(
            //Observable.Return(devices),
            //DataFlow.Render,
            //_audioDeviceManager.SetDefaultAudioEndpoint))
            //;
            //Playback = playback;
            //Recording = recording;

            var observableManager = Observable.FromAsync(async () => await GlobalSystemMediaTransportControlsSessionManager.RequestAsync())
                .Replay(1)
                .RefCount();
            IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> sessionsListUpdates = ObserveSessionsList(observableManager)
                .Do(_ => Debug.WriteLine("Session list updated"));
            // bind sessions list

            BindSessionsList(sessionsListUpdates)
                .DisposeWith(disposables);

            var currentSessionChanged = ObserveCurrentSession(observableManager);


            var sessionSelections = this
                .WhenAnyValue(vm => vm.ActiveSession, true)
                .Do(x => Debug.WriteLine($"Active Session changed {x?.Id}"))
                .WithLatestFrom(sessionsListUpdates)
                .DistinctUntilChanged(x => x.First?.Id)
                .Select(s =>
                {
                    var active = s.First;
                    var list = s.Second;
                    if (active is null)
                    {
                        return null;
                    }

                    var session = list.FirstOrDefault(x => x.SourceAppUserModelId == active.Id);

                    return session;

                })
                .DistinctUntilChanged();

            sessionSelections
                .Where(session => session is not null)
                .DistinctUntilChanged(x => x?.SourceAppUserModelId)
                .Select(session => Observable.Create<MediaSessionViewModel>((observer) =>
                {
                    var aumid = session?.SourceAppUserModelId;
                    if (aumid is not null)
                    {
                        var app = _applicationService.ApplicationsByAumid.Lookup(aumid);
                        if (!app.HasValue)
                        {
                            return Disposable.Empty;
                        }

                        var matchingSessions = sessions.Watch(aumid);
                        var vm = new MediaSessionViewModel(session!, matchingSessions);

                        observer.OnNext(vm);
                        return vm;
                    }

                    return Disposable.Empty;
                }))
                .Do(_ => Debug.WriteLine("New media session viewmodel observable"))
                .Switch()
                .Do(x => Debug.WriteLine($"Switching to session {x?.Title}"))

                .ToProperty(this, vm => vm.SessionData, out _sessionData, initialValue: null);


            currentSessionChanged
                .Where(session => session is not null)
                .Select(session => (session, _applicationService.ApplicationsByAumid.Lookup(session.SourceAppUserModelId)))
                .Where(values => values.Item2.HasValue)
                .Subscribe(sessionOption =>
                {
                    //Debug.WriteLine($"WinRT new session: {session.SourceAppUserModelId}");
                    //foreach (var s in Sessions)
                    //{
                    //Debug.WriteLine($"sessoin: {s.Id}: {s.Name}");

                    //}
                    ActiveSession = SessionItem.Create(sessionOption.session, sessionOption.Item2.Value); //Sessions.FirstOrDefault(x => x.Id == session.SourceAppUserModelId)!;
                                                                                                          //var mediaPropertyChanges = Observable.FromEventPattern<MediaPropertiesChangedEventArgs>(session, nameof(session.MediaPropertiesChanged))

                },
                ex =>
                {
                    Debug.WriteLine($"Failed to set active session {ex.Message}");
                }).DisposeWith(disposables);
        });



    }





    private static IObservable<GlobalSystemMediaTransportControlsSession> ObserveCurrentSession(IObservable<GlobalSystemMediaTransportControlsSessionManager> observableManager)
    {


        // Handle WinRT session change
        return observableManager
            .Select(manager =>
                EventHelper.EventOrEmpty<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs, GlobalSystemMediaTransportControlsSession>(
                    manager,
                    h => manager.CurrentSessionChanged += h,
                    h => manager.CurrentSessionChanged -= h,
                    events => events.Select(_ => manager.GetCurrentSession())
                    ))
                .Switch()
                .Do(x =>
                {
                    Debug.WriteLine($"Session changed event: {x?.SourceAppUserModelId}");
                })
                .DistinctUntilChanged(x => x?.SourceAppUserModelId)
                .Do(x =>
                {
                    Debug.WriteLine($"WINRT: Current session changed. Got new session {x?.SourceAppUserModelId ?? "no session"}");
                })
                .Replay(1)
                .RefCount()
                .ObserveOn(RxApp.MainThreadScheduler);
    }

    private IDisposable BindSessionsList(IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> sessionsListUpdates)
    {
        return sessionsListUpdates
                        //.ObserveOn(RxApp.TaskpoolScheduler)
                        .ObserveOn(RxApp.MainThreadScheduler)
                        .Subscribe(sessions =>
                            {

                                var x = sessions.Select(session =>
                                {
                                    var appOption = _applicationService.ApplicationsByAumid.Lookup(session.SourceAppUserModelId);
                                    InstalledApplicationInfo app;
                                    if (!appOption.HasValue)
                                    {
                                        app = new InstalledApplicationInfo
                                        {
                                            AppUserModelId = session.SourceAppUserModelId,
                                            Name = session.SourceAppUserModelId,
                                            PackageInstallPath = null,
                                            TargetPath = null,
                                            Icon = InstalledApplicationService.LoadingImage
                                        };
                                    }
                                    else
                                    {
                                        app = appOption.Value;
                                    }
                                    return SessionItem.Create(session, app);
                                }).ToArray();
                                Sessions = x;
                            },
                            ex =>
                            {
                                Debug.WriteLine($"Failed to get icon due to {ex.Message}");
                            });
    }

    private static IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> ObserveSessionsList(IObservable<GlobalSystemMediaTransportControlsSessionManager> observableManager)
    {
        return observableManager
            .Select(manager => Observable.FromEventPattern<SessionsChangedEventArgs>(manager, nameof(manager.SessionsChanged))
                .Select(_ => Unit.Default)
                .StartWith(Unit.Default)
                .Select(_ => manager.GetSessions())
                .Do(sessions => { Debug.WriteLine(string.Join(", ", sessions.Select(session => session.SourceAppUserModelId).ToArray())); })
            )
            .Replay(1)
            .RefCount()
            .Switch();
    }

    private void HandleDeactivation()
    {
        _mediaControlsStateService.HideView();
    }

    //public AudioDeviceSelectorViewModel.DeviceItem[] PlaybackDevices => _playbackDevices?.Value ?? [];
    //public DeviceItem[] RecordingDevices => _recordingDevices?.Value ?? [];

    public SessionItem ActiveSession
    {
        get => _activeSession;
        set => this.RaiseAndSetIfChanged(ref _activeSession, value);
    }

    public IReadOnlyList<SessionItem> Sessions
    {
        get => _sessions;
        set => this.RaiseAndSetIfChanged(ref _sessions, value);
    }
    public MediaSessionViewModel? SessionData
    {
        get => _sessionData?.Value ?? null;
    }

    //private MMDevice? GetDefaultPlaybackDevice()
    //{
    //    try
    //    {
    //        return _deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    //    }
    //    catch (Exception ex)
    //    {
    //        Debug.WriteLine("Error getting default playback device:");
    //        Debug.WriteLine(ex);
    //        return null;
    //    }
    //}
    //private MMDeviceCollection GetDevices(DataFlow dataFlow)
    //{
    //    var devices = _deviceEnum.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
    //    return devices;
    //}

    //private float GetVolume()
    //{
    //    var device = GetDefaultPlaybackDevice();

    //    if (device is not null)
    //    {
    //        return device.AudioEndpointVolume?.MasterVolumeLevelScalar ?? 0;
    //    }

    //    return 0;
    //}

    //private async Task SetVolume(float volume)
    //{
    //    var device = GetDefaultPlaybackDevice();
    //    if (device?.AudioEndpointVolume is not null)
    //    {
    //        device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
    //    }

    //}






    private IObservable<Unit> PlayPauseImpl()
    {
        //if(ActiveSession is not null)
        //{
        return Observable.Start(MediaKeySender.PlayPause);
        //}

        //ActiveSession
    }

    private IObservable<Unit> PrevImpl()
    {
        return Observable.Start(MediaKeySender.Prev);

    }

    private IObservable<Unit> NextImpl()
    {
        return Observable.Start(MediaKeySender.Next);
    }


}
