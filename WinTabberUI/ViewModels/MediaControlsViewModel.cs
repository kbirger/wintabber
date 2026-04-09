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
using WinTabberUI.ViewModels.Factories;
using WinTabber.Common.Util;

namespace WinTabberUI.ViewModels;

public class MediaControlsViewModel : ReactiveObject, IActivatableViewModel
{
    private ReadOnlyObservableCollection<SessionListItem> _sessions =
           new ReadOnlyObservableCollection<SessionListItem>([]);
    private MediaSessionViewModel? _activeSession;
    private readonly MediaSessionService _mediaSessionService;
    private readonly IMediaControlsStateService _mediaControlsStateService;
    private readonly MediaSessionViewModelFactory _mediaSessionViewModelFactory;
    private readonly AudioDeviceSelectorViewModelFactory _deviceSelectorViewModelFactory;

    //private readonly IAudioDeviceManager _audioDeviceManager;
    private readonly CompositeDisposable _cleanUp;
    private AudioDeviceSelectorViewModel? _playback;
    private AudioDeviceSelectorViewModel? _recording;

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
    //public ReactiveCommand<Unit, Unit> PlayPause { get; private set; }
    //public ReactiveCommand<Unit, Unit> Next { get; private set; }
    //public ReactiveCommand<Unit, Unit> Prev { get; private set; }
    //public ReactiveCommand<Unit, Unit> Mute { get; private set; }

    public MediaControlsViewModel(
        MediaSessionService mediaSessionService,
        IMediaControlsStateService mediaControlsStateService,
        MediaSessionViewModelFactory mediaSessionViewModelFactory,
        AudioDeviceSelectorViewModelFactory deviceSelectorViewModelFactory,
        WinTabberEventManager eventManager

    )
    {
        PropertyChanged += MediaControlsViewModel_PropertyChanged;
        _mediaSessionService = mediaSessionService;
        _mediaControlsStateService = mediaControlsStateService;
        _mediaSessionViewModelFactory = mediaSessionViewModelFactory;
        _deviceSelectorViewModelFactory = deviceSelectorViewModelFactory;
        _cleanUp = new CompositeDisposable();
        var scheduler = RxSchedulers.MainThreadScheduler;

        Debug.WriteLine("Created");
        //this.WhenActivated((disposables) =>
        {
            Debug.WriteLine("Activated");
            ActiveSession = null;

            var sessions = _mediaSessionService.MasterSessions
                .Connect()
                .Transform(session => new SessionListItem(session));

            sessions
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Bind(out _sessions).Subscribe().DisposeWith(_cleanUp);

            var activeSessionChanges = _mediaSessionService.ActiveSession
                .Select(session => sessions
                    .WatchValue(session.MediaSession.SourceAppUserModelId)
                    .Log(s => $"Session watch update: {s.Aumid} - {s.Session.NativeSession != null}"))
                .Switch()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Publish()
                .RefCount();
            activeSessionChanges
                .Subscribe(
                    changedSession =>
                    {
                        SelectedSessionListItem = changedSession;
                    },
                    ex =>
                    {
                        Debug.WriteLine("Error in ActiveSession pipeline: {0}", ex);
                    }
                )
                .DisposeWith(_cleanUp);

            activeSessionChanges
                .Select(changedSession => Observable.Using(
                    () => _mediaSessionViewModelFactory.Create(changedSession.Session), 
                    sessionViewModel => Observable.Return(sessionViewModel).Concat(Observable.Never<MediaSessionViewModel>())))
                .Switch()
                .Subscribe(
                    viewModel =>
                    {
                        ActiveSession = viewModel;
                    },
                    ex =>
                    {
                        Debug.WriteLine("Error in ActiveSession pipeline2: {Error}", ex);
                    }
                )
                .DisposeWith(_cleanUp);

            _playback = deviceSelectorViewModelFactory.Create(DataFlow.Render);
            _recording = deviceSelectorViewModelFactory.Create(DataFlow.Capture);
            // END
        }
    }

    private void MediaControlsViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        //throw new NotImplementedException();
    }

    private void HandleDeactivation()
    {
        _mediaControlsStateService.HideView();
    }

    //public AudioDeviceSelectorViewModel.DeviceItem[] PlaybackDevices => _playbackDevices?.Value ?? [];
    //public DeviceItem[] RecordingDevices => _recordingDevices?.Value ?? [];

    public MediaSessionViewModel? ActiveSession
    {
        get => _activeSession;
        set => this.RaiseAndSetIfChanged(ref _activeSession, value);
    }

    public ReadOnlyObservableCollection<SessionListItem> Sessions
    {
        get => _sessions;
        set => this.RaiseAndSetIfChanged(ref _sessions, value);
    }

    public SessionListItem SelectedSessionListItem
    {
        get => field;
        set => this.RaiseAndSetIfChanged(ref field, value);
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

    //private IObservable<Unit> PlayPauseImpl()
    //{
    //    //if(ActiveSession is not null)
    //    //{
    //    return Observable.Start(MediaKeySender.PlayPause);
    //    //}

    //    //ActiveSession
    //}

    //private IObservable<Unit> PrevImpl()
    //{
    //    return Observable.Start(MediaKeySender.Prev);
    //}

    //private IObservable<Unit> NextImpl()
    //{
    //    return Observable.Start(MediaKeySender.Next);
    //}
}
