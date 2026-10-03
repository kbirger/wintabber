using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
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
    private SessionListItem? _selectedSessionListItem;

    // The user's pick input channel. Assigned in WhenActivated, cleared on deactivation --
    // the public setter cannot see the activation closure, so this needs to be a field.
    private BehaviorSubject<string?>? _userPick;

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

        Debug.WriteLine("Created");
        this.WhenActivated((disposables) =>
        {
            ActiveSession = null;

            // The user's pick, as an AUMID. A string, not a SessionListItem: the item object is
            // rebuilt whenever the session leaves and re-enters the cache, which a track skip does
            // for anywhere between 62ms and 950ms (measured). An AUMID survives that; an object
            // reference does not.
            //
            // Null means "follow whatever SMTC reports as active".
            //
            // Disposed explicitly in the teardown below, after _userPick is nulled out -- not via
            // DisposeWith(disposables) here, which would race the field clear: CompositeDisposable
            // has no ordering guarantee against a second, separately-registered disposable, so the
            // setter could observe a non-null _userPick pointing at an already-disposed subject and
            // throw ObjectDisposedException on OnNext.
            var userPick = new BehaviorSubject<string?>(null);
            _userPick = userPick;

            // Materialized into a cache, not left as a cold chain: Bind below and every
            // WatchValue in the ActiveSession pipeline read from this one cache, so Transform runs
            // once and there is exactly one SessionListItem per session. SelectedSessionListItem
            // then holds the same instance the ComboBox shows, instead of an equal-by-Aumid twin.
            //
            // REAL BUG this fixes: with a cold chain, each subscriber ran its own Transform, so the
            // WatchValue chain built a parallel set of SessionListItems. Switch disposing the
            // previous WatchValue subscription made DisposeMany dispose the very item
            // SelectedSessionListItem pointed at -- on every ActiveSession emission, which includes
            // a track skip. Verified with a reduced DynamicData repro of this exact shape: three
            // instances for two emissions, the selected one disposed each time, while the bound
            // item was a different object that DisposeMany never touched.
            //
            // DisposeMany sits upstream of AsObservableCache, so disposing the cache (tied to this
            // activation via disposables) still disposes every SessionListItem, as before.
            var sessionCache = _mediaSessionService
                .MasterSessions.Connect()
                .Transform(session => new SessionListItem(session))
                .DisposeMany()
                .AsObservableCache();
            sessionCache.DisposeWith(disposables);

            var sessions = sessionCache.Connect();

            // ResetThreshold: int.MaxValue, matching AudioDeviceSelectorViewModel's own Devices
            // binding fix -- Bind() collapses a large-enough simultaneous changeset into a single
            // CollectionChanged Reset instead of granular Add/Remove, and WinUI 3's Selector-derived
            // ComboBox clears SelectedItem on Reset. Sessions add/remove in a batch the same way
            // devices do (MasterSessions.AutoRefreshOnObservable refreshes broadly), so this is
            // preventive, not (yet) reproduced live the way the device-list case was.
            //
            // Both this and the RaisePropertyChanged(nameof(Sessions)) call below are kept under
            // review, see the follow-up section dated 2026-09-24 in
            // docs/superpowers/plans/2026-09-12-wpf-to-winui3-migration.md.
            sessions
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Bind(out _sessions, new BindingOptions(ResetThreshold: int.MaxValue))
                .Subscribe()
                .DisposeWith(disposables);
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
            var activeAumid = _mediaSessionService.ActiveSession.Select(session =>
                session.MediaSession.SourceAppUserModelId
            );

            // The effective selection: the user's pick when that session is in the cache, otherwise
            // the SMTC-active one. Re-resolved on every cache change, so a session that leaves and
            // returns simply resolves again -- no timer, no remembered key, nothing timing-dependent.
            //
            // Replaces DistinctUntilChanged(session => session?.Session.Key). That gate could only
            // let a restore through when the key happened to change, and Key is (IsComplete, Aumid)
            // -- so recovery depended on whether the native audio session incidentally dropped
            // alongside the SMTC one. Verified in a trace: identical removals, opposite outcomes,
            // decided by that flap.
            var effectiveSelection = Observable
                .CombineLatest(userPick, activeAumid, (pick, active) => (Pick: pick, Active: active))
                .Select(inputs =>
                    sessionCache
                        .Connect()
                        .ToCollection()
                        .Select(items => Resolve(items, i => i.Aumid, inputs.Pick, inputs.Active))
                )
                .Switch()
                // Reference equality, explicit: SessionListItem.Equals compares by Aumid, so the
                // default comparer would swallow a same-Aumid-different-instance rebuild (a same-
                // batch Remove+Add with no intervening null) instead of picking up the new instance.
                // A Refresh rebuilds no instances, so on the ordinary path this suppresses only the
                // churn (770 refreshes in one 90-second trace); ReferenceEqualityComparer keeps that
                // guarantee honest instead of accidentally depending on Aumid equality too.
                .DistinctUntilChanged<SessionListItem?>(ReferenceEqualityComparer.Instance)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Publish()
                .RefCount();

            effectiveSelection
                .Subscribe(
                    SetSelectionFromModel,
                    ex => Debug.WriteLine("Error in selection pipeline: {0}", ex)
                )
                .DisposeWith(disposables);

            var activeSession = _mediaSessionViewModelFactory.Create();
            ActiveSession = activeSession;
            effectiveSelection
                .Subscribe(
                    item => ActiveSession.Session = item?.Session,
                    ex => Debug.WriteLine("Error in ActiveSession pipeline: {0}", ex)
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
                    _userPick = null;
                    userPick.Dispose();
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
        get => _selectedSessionListItem;
        set
        {
            // Only the view writes here. A null is never user intent: this ComboBox has no empty
            // entry, so null only ever arrives when the bound item left the collection, which
            // happens on every track skip. Dropping it is the whole point -- the derived pipeline
            // decides what is selected.
            if (value is null)
            {
                return;
            }

            _userPick?.OnNext(value.Aumid);
            this.RaiseAndSetIfChanged(ref _selectedSessionListItem, value);
        }
    }

    /// <summary>
    /// Writes the derived selection without treating it as a user pick. The public setter is the
    /// view's input channel; this is the model's output channel. Keeping them separate is what
    /// removes the need to guess where a write came from.
    /// </summary>
    private void SetSelectionFromModel(SessionListItem? item)
    {
        this.RaiseAndSetIfChanged(ref _selectedSessionListItem, item, nameof(SelectedSessionListItem));
    }

    // The pick wins while its session exists. It is deliberately NOT cleared when the session
    // is briefly absent -- that absence is exactly the track-skip gap this fix exists for.
    //
    // internal, not private, and generic over the AUMID projection rather than tied to
    // SessionListItem directly: WinTabber.UI.Media.Tests exercises this directly (see the "Known
    // risk" note in the selection-model plan), but SessionListItem's own constructor takes a real
    // AggregateSession, which wraps a WinRT session type the test project cannot build (the same
    // constraint FakeMediaSessionService documents). The projection lets a test pass a trivial
    // stand-in instead of a reflection-built SessionListItem, without changing what the pipeline
    // passes in at the real call site.
    internal static T? Resolve<T>(IReadOnlyCollection<T> items, Func<T, string> aumid, string? pick, string? active)
        where T : class
    {
        var picked =
            pick is null
                ? null
                : items.FirstOrDefault(i => string.Equals(aumid(i), pick, StringComparison.OrdinalIgnoreCase));
        if (picked is not null)
        {
            return picked;
        }

        return active is null
            ? null
            : items.FirstOrDefault(i => string.Equals(aumid(i), active, StringComparison.OrdinalIgnoreCase));
    }
}
