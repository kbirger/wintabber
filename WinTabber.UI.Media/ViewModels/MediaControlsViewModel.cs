using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using DynamicData;
using NAudio.CoreAudioApi;
using ReactiveUI;
using WinTabber.Common.Util;
using WinTabber.Events;
using WinTabber.UI.Media.Services;
using WinTabber.UI.Media.ViewModels.Factories;
using WinTabberUI.Services;

namespace WinTabber.UI.Media.ViewModels;

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

            var sessions = _mediaSessionService
                .MasterSessions.Connect()
                .Transform(session => new SessionListItem(session));

            sessions.ObserveOn(RxSchedulers.MainThreadScheduler).Bind(out _sessions).Subscribe().DisposeWith(_cleanUp);
            _sessions
                .ActOnEveryObject(
                    (x) =>
                    {
                        Debug.WriteLine("add");
                    },
                    (x) =>
                    {
                        Debug.WriteLine("remove");
                    }
                )
                .DisposeWith(_cleanUp);
            // Watch for SMTC session changes and match against known sessions.
            var activeSessionChanges = _mediaSessionService
                .ActiveSession.Select(session =>
                    sessions
                        .WatchValue(session.MediaSession.SourceAppUserModelId)
                        .Log(s => $"Session watch update: {s.Aumid} - {s.Session.NativeSession != null}")
                )
                .Switch()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Publish()
                .RefCount();

            // Update selected session when active session changes
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

            // Create or dispose session view model when active session changes
            // or when user selects a different session from the list
            ActiveSession = _mediaSessionViewModelFactory.Create();
            this.WhenAnyValue(vm => vm.SelectedSessionListItem)
                .Merge(activeSessionChanges)
                .Throttle(TimeSpan.FromMilliseconds(250))
                .DistinctUntilChanged(session => session?.Session.Key)
                .ObserveOn(scheduler)
                //.Select(changedSession =>
                //    changedSession is not null
                //        ? Observable.Using(
                //            () => _mediaSessionViewModelFactory.Create(changedSession.Session),
                //            sessionViewModel =>
                //                Observable.Return(sessionViewModel).Concat(Observable.Never<MediaSessionViewModel>())
                //        )
                //        : Observable.Empty<MediaSessionViewModel>()
                //)
                //.Switch()
                .Subscribe(
                    viewModel =>
                    {
                        ActiveSession.Session = viewModel?.Session;
                    },
                    ex =>
                    {
                        Debug.WriteLine("Error in ActiveSession pipeline2: {0}", ex);
                    }
                )
                .DisposeWith(_cleanUp);

            _playback = deviceSelectorViewModelFactory.Create(DataFlow.Render);
            _recording = deviceSelectorViewModelFactory.Create(DataFlow.Capture);
            // END
        }
    }

    private void MediaControlsViewModel_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e
    )
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

    public SessionListItem? SelectedSessionListItem
    {
        get => field;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

}
