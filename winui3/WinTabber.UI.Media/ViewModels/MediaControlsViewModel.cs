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

namespace WinTabber.UI.Media.ViewModels;

public class MediaControlsViewModel : ReactiveObject, IActivatableViewModel, IDisposable
{
    private ReadOnlyObservableCollection<SessionListItem> _sessions =
        new ReadOnlyObservableCollection<SessionListItem>([]);
    private MediaSessionViewModel? _activeSession;
    private readonly IMediaSessionService _mediaSessionService;
    private readonly MediaSessionViewModelFactory _mediaSessionViewModelFactory;
    private readonly AudioDeviceSelectorViewModelFactory _deviceSelectorViewModelFactory;

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

    public MediaControlsViewModel(
        IMediaSessionService mediaSessionService,
        MediaSessionViewModelFactory mediaSessionViewModelFactory,
        AudioDeviceSelectorViewModelFactory deviceSelectorViewModelFactory,
        WinTabberEventManager eventManager
    )
    {
        PropertyChanged += MediaControlsViewModel_PropertyChanged;
        _mediaSessionService = mediaSessionService;
        _mediaSessionViewModelFactory = mediaSessionViewModelFactory;
        _deviceSelectorViewModelFactory = deviceSelectorViewModelFactory;
        var scheduler = RxSchedulers.MainThreadScheduler;

        Debug.WriteLine("Created");
        this.WhenActivated((disposables) =>
        {
            Debug.WriteLine("Activated");
            ActiveSession = null;

            var sessions = _mediaSessionService
                .MasterSessions.Connect()
                .Transform(session => new SessionListItem(session));

            sessions.ObserveOn(RxSchedulers.MainThreadScheduler).Bind(out _sessions).Subscribe().DisposeWith(disposables);
            // Bind(out _sessions) writes the field directly, bypassing the Sessions property
            // setter -- RaiseAndSetIfChanged never runs, so PropertyChanged(nameof(Sessions)) never
            // fires, and the classic {Binding Sessions} in MediaControlsWindow.xaml (already bound
            // by the time this activation runs) never sees the real, live collection. Confirmed live:
            // Playback/Recording populate correctly because they go through their own property
            // setters (Playback = playback; below); Sessions did not, and its ComboBox stayed empty.
            this.RaisePropertyChanged(nameof(Sessions));
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
                .DisposeWith(disposables);
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
                .DisposeWith(disposables);

            // Create or dispose session view model when active session changes
            // or when user selects a different session from the list
            var activeSession = _mediaSessionViewModelFactory.Create();
            ActiveSession = activeSession;
            this.WhenAnyValue(vm => vm.SelectedSessionListItem)
                .Merge(activeSessionChanges)
                .Throttle(TimeSpan.FromMilliseconds(250))
                .DistinctUntilChanged(session => session?.Session.Key)
                .ObserveOn(scheduler)
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
                .DisposeWith(disposables);

            var playback = deviceSelectorViewModelFactory.Create(DataFlow.Render);
            var recording = deviceSelectorViewModelFactory.Create(DataFlow.Capture);
            // Through the properties, not the backing fields: XAML binds Playback/Recording
            // directly, and a reactivation must notify the view of the new instance.
            Playback = playback;
            Recording = recording;

            // Deactivation must release what this activation created: without this, a second
            // activation (the window is shown again) would build a second pair of device
            // selectors and a second ActiveSession on top of ones nothing ever disposed.
            Disposable
                .Create(() =>
                {
                    playback.Dispose();
                    recording.Dispose();
                    activeSession.Dispose();
                    Playback = null;
                    Recording = null;
                    ActiveSession = null;
                })
                .DisposeWith(disposables);
        });
    }

    /// <summary>
    /// Safety net for the case this view model is disposed while still activated — e.g. the
    /// process exits without the window ever hiding. The ordinary path disposes
    /// <c>_playback</c>/<c>_recording</c> on deactivation (see the <c>WhenActivated</c> block
    /// above); this makes double-disposing them, here, harmless if that path never ran.
    /// </summary>
    public void Dispose()
    {
        _playback?.Dispose();
        _recording?.Dispose();
    }

    private void MediaControlsViewModel_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e
    )
    {
    }

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
