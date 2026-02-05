using DynamicData;
using DynamicData.Binding;
using NAudio.CoreAudioApi;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
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
    //private IObservableList<SessionItem> _sessions ;
    private ReadOnlyObservableCollection<MediaSession> _sessions = new ReadOnlyObservableCollection<MediaSession>([]);
    private MediaSession _activeSession;
    private readonly InstalledApplicationRepository _applicationService;
    private readonly IMediaControlsStateService _mediaControlsStateService;
    private readonly IAudioDeviceManager _audioDeviceManager;
    private readonly CompositeDisposable _cleanUp;
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





    public MediaControlsViewModel(InstalledApplicationRepository applicationService, IMediaControlsStateService mediaControlsStateService, IAudioDeviceManager audioDeviceManager, WinTabberEventManager eventManager)
    {
        _applicationService = applicationService;
        _mediaControlsStateService = mediaControlsStateService;
        _audioDeviceManager = audioDeviceManager;
        var scheduler = RxApp.MainThreadScheduler;


        Debug.WriteLine("Created");
        //this.WhenActivated((disposables) =>
        {
            Debug.WriteLine("Activated");



            var ad = _audioDeviceManager.Connect();
            var renderDevices = ad.Filter(x => x.Kind == DataFlow.Render);
            var recordingDevices = ad.Filter(x => x.Kind == DataFlow.Capture);
            //var sessions = ad.TransformMany(device => new DeviceSessionWatcher(device.Device.AudioSessionManager, applicationService.ApplicationsByPath).Connect().AsObservableCache(), x => x.AumId).AsObservableCache();

            _playback = new AudioDeviceSelectorViewModel(renderDevices);
            _recording = new AudioDeviceSelectorViewModel(recordingDevices);

            var observableManager = Observable.FromAsync(async () => await GlobalSystemMediaTransportControlsSessionManager.RequestAsync())
                .Replay(1)
                .RefCount();

            var sessionsListUpdates = ObserveSessionsList(observableManager)
                .Do(_ => Debug.WriteLine($"Session list updated {_sessions?.Count}"))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SubscribeOn(RxApp.MainThreadScheduler);
            // bind sessions list

            var sSessions = SessionChangesToCache(sessionsListUpdates)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out _sessions)
                .Subscribe();

            //.BindTo(this, (vm) => vm.Sessions)

            //.Bind(out _sessions);

            //BindSessionsList(sessionsListUpdates)
            //    .DisposeWith(disposables);

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
                        //var app = _applicationService.ApplicationsByAumid.Lookup(aumid);
                        //if (!app.HasValue)
                        //{
                        //return Disposable.Empty;
                        //}

                        //var matchingSessions = sessions.Watch(aumid);
                        var vm = new MediaSessionViewModel(session!, Observable.Empty<Change<AudioSession, string>>());

                        observer.OnNext(vm);
                        return vm;
                    }

                    return Disposable.Empty;
                }))
                .Do(_ => Debug.WriteLine("New media session viewmodel observable"))
                .Switch()
                .Do(x => Debug.WriteLine($"Switching to session {x?.Title}"))

                .ToProperty(this, vm => vm.SessionData, out _sessionData, initialValue: null);


            var sSession = currentSessionChanged
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
                    ActiveSession = MediaSession.Create(sessionOption.session, sessionOption.Item2.Value); //Sessions.FirstOrDefault(x => x.Id == session.SourceAppUserModelId)!;
                                                                                                          //var mediaPropertyChanges = Observable.FromEventPattern<MediaPropertiesChangedEventArgs>(session, nameof(session.MediaPropertiesChanged))

                },
                ex =>
                {
                    Debug.WriteLine($"Failed to set active session {ex.Message}");
                });
            //});


            _cleanUp = new CompositeDisposable(
                sSessions,
                sSession,
                Disposable.Create(() => HandleDeactivation())
            );
        }
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
                                Icon = InstalledApplicationRepository.LoadingImage
                            };
                        }
                        else
                        {
                            app = appOption.Value;
                        }
                        return MediaSession.Create(session, app);
                    }).ToArray();
                    //Sessions = x;
                },
                ex =>
                {
                    Debug.WriteLine($"Failed to get icon due to {ex.Message}");
                });
    }


    private IObservable<IChangeSet<MediaSession, string>> SessionChangesToCache(IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> sessionUpdates)
    {
        return ObservableChangeSet.Create<MediaSession, string>(
            (cache) =>
            {
                return sessionUpdates.Subscribe(sessions =>
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
                                Icon = InstalledApplicationRepository.LoadingImage
                            };
                        }
                        else
                        {
                            app = appOption.Value;
                        }
                        return MediaSession.Create(session, app);
                    }).ToArray();

                    cache.Edit((updater) =>
                        {
                            updater.Clear();
                            updater.AddOrUpdate(x);
                        });
                });
            },
            (session) => session.Id
        ).AutoRefresh();
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
            .Switch()
            .Replay(1)
            .RefCount();
    }

    private void HandleDeactivation()
    {
        _mediaControlsStateService.HideView();
    }

    //public AudioDeviceSelectorViewModel.DeviceItem[] PlaybackDevices => _playbackDevices?.Value ?? [];
    //public DeviceItem[] RecordingDevices => _recordingDevices?.Value ?? [];

    public MediaSession ActiveSession
    {
        get => _activeSession;
        set => this.RaiseAndSetIfChanged(ref _activeSession, value);
    }

    public ReadOnlyObservableCollection<MediaSession> Sessions
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
