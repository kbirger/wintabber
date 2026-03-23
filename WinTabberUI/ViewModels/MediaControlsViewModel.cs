using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows;
using DynamicData;
using DynamicData.Binding;
using NAudio.CoreAudioApi;
using ReactiveUI;
using Windows.Media.Control;
using WinTabber.Api.Media;
using WinTabber.Api.Media.CoreAudio.Services;
using WinTabber.Api.Media.Repositories;
using WinTabber.Api.Media.ShellApplications.Models;
using WinTabber.Api.Media.ShellApplications.Repositories;
using WinTabber.Events;
using WinTabber.Interop;
using WinTabberUI.Infrastructure;
using WinTabberUI.Models;
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
    private ReadOnlyObservableCollection<SessionListItem> _sessions =
        new ReadOnlyObservableCollection<SessionListItem>([]);
    private MediaSessionViewModel _activeSession;
    private readonly InstalledApplicationRepository _applicationService;
    private readonly MediaSessionService _mediaSessionService;
    private readonly IMediaControlsStateService _mediaControlsStateService;
    //private readonly IAudioDeviceManager _audioDeviceManager;
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

    public MediaControlsViewModel(
        InstalledApplicationRepository applicationService,
        MediaSessionService mediaSessionService,
        IMediaControlsStateService mediaControlsStateService,
        CoreAudioDeviceRepository coreAudioDeviceRepository,
        AudioSessionService audioSessionService,
        AudioDeviceService audioDeviceService,
        WinTabberEventManager eventManager
    )
    {
        _applicationService = applicationService;
        _mediaSessionService = mediaSessionService;
        _mediaControlsStateService = mediaControlsStateService;
        _cleanUp = new CompositeDisposable();
        var scheduler = RxSchedulers.MainThreadScheduler;

        Debug.WriteLine("Created");
        //this.WhenActivated((disposables) =>
        {
            Debug.WriteLine("Activated");

            var sessions = _mediaSessionService.MasterSessions
                .Transform(session => new SessionListItem(session));

            sessions
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Bind(out _sessions).Subscribe();

            _mediaSessionService.ActiveSession
                .Select(session => sessions.Watch(session.MediaSession.SourceAppUserModelId)
                    .Select(change => change.Current))
                .Switch()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(changedSession => ActiveSession = new MediaSessionViewModel(changedSession.Session, audioSessionService, audioDeviceService))
                .DisposeWith(_cleanUp);

            var ad = coreAudioDeviceRepository.Devices
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Publish()
                .AutoConnect();

            var c = ad.AsObservableCache();
            var d = c.Connect();
            //var ad = _audioDeviceManager.Connect();

            // todo: port to new code
            //var renderDevices = d.Filter(x => x.DataFlow== DataFlow.Render).Transform(d => new DeviceItem(d));
            //var recordingDevices = d.Filter(x => x.DataFlow == DataFlow.Capture).Transform(d => new DeviceItem(d));


            ////var sessions = ad.TransformMany(device => new DeviceSessionWatcher(device.Device.AudioSessionManager, applicationService.ApplicationsByPath).Connect().AsObservableCache(), x => x.AumId).AsObservableCache();
            //_playback = new AudioDeviceSelectorViewModel(renderDevices);
            //_recording = new AudioDeviceSelectorViewModel(recordingDevices);
            // END




        }
    }

    private static IObservable<GlobalSystemMediaTransportControlsSession> ObserveCurrentSession(
        IObservable<GlobalSystemMediaTransportControlsSessionManager> observableManager
    )
    {
        // Handle WinRT session change
        return observableManager
            .Select(manager =>
                EventHelper.EventOrEmpty<
                    GlobalSystemMediaTransportControlsSessionManager,
                    CurrentSessionChangedEventArgs,
                    GlobalSystemMediaTransportControlsSession
                >(
                    manager,
                    h => manager.CurrentSessionChanged += h,
                    h => manager.CurrentSessionChanged -= h,
                    events => events.Select(_ => manager.GetCurrentSession())
                )
            )
            .Switch()
            .Do(x =>
            {
                Debug.WriteLine($"Session changed event: {x?.SourceAppUserModelId}");
            })
            .DistinctUntilChanged(x => x?.SourceAppUserModelId)
            .Do(x =>
            {
                Debug.WriteLine(
                    $"WINRT: Current session changed. Got new session {x?.SourceAppUserModelId ?? "no session"}"
                );
            })
            .Replay(1)
            .RefCount()
            .ObserveOn(RxSchedulers.MainThreadScheduler);
    }

    private IDisposable BindSessionsList(
        IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> sessionsListUpdates
    )
    {
        return sessionsListUpdates
        //.ObserveOn(RxSchedulers.TaskpoolScheduler)
        .Subscribe(
            sessions =>
            {
                var x = sessions
                    .Select(session =>
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
                                Icon = InstalledApplicationRepository.LoadingImage,
                            };
                        }
                        else
                        {
                            app = appOption.Value;
                        }
                        return MediaSessionVm.Create(session, app);
                    })
                    .ToArray();
                //Sessions = x;
            },
            ex =>
            {
                Debug.WriteLine($"Failed to get icon due to {ex.Message}");
            }
        );
    }

    private IObservable<IChangeSet<MediaSessionVm, string>> SessionChangesToCache(
        IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> sessionUpdates
    )
    {
        return ObservableChangeSet
            .Create<MediaSessionVm, string>(
                (cache) =>
                {
                    return sessionUpdates.Subscribe(sessions =>
                    {
                        var x = sessions
                            .Select(session =>
                            {
                                var appOption = _applicationService.ApplicationsByAumid.Lookup(
                                    session.SourceAppUserModelId
                                );
                                InstalledApplicationInfo app;
                                if (!appOption.HasValue)
                                {
                                    app = new InstalledApplicationInfo
                                    {
                                        AppUserModelId = session.SourceAppUserModelId,
                                        Name = session.SourceAppUserModelId,
                                        PackageInstallPath = null,
                                        TargetPath = null,
                                        Icon = InstalledApplicationRepository.LoadingImage,
                                    };
                                }
                                else
                                {
                                    app = appOption.Value;
                                }
                                return MediaSessionVm.Create(session, app);
                            })
                            .ToArray();

                        cache.Edit(
                            (updater) =>
                            {
                                updater.Clear();
                                updater.AddOrUpdate(x);
                            }
                        );
                    });
                },
                (session) => session.Id
            )
            .AutoRefresh();
    }

    private static IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> ObserveSessionsList(
        IObservable<GlobalSystemMediaTransportControlsSessionManager> observableManager
    )
    {
        return observableManager
            .Select(manager =>
                Observable
                    .FromEventPattern<SessionsChangedEventArgs>(manager, nameof(manager.SessionsChanged))
                    .Select(_ => Unit.Default)
                    .StartWith(Unit.Default)
                    .Select(_ => manager.GetSessions())
                    .Do(sessions =>
                    {
                        Debug.WriteLine(
                            string.Join(", ", sessions.Select(session => session.SourceAppUserModelId).ToArray())
                        );
                    })
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

    public MediaSessionViewModel ActiveSession
    {
        get => _activeSession;
        set => this.RaiseAndSetIfChanged(ref _activeSession, value);
    }

    public ReadOnlyObservableCollection<SessionListItem> Sessions
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
