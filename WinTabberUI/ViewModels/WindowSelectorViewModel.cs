using DynamicData;
using ReactiveUI;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows.Forms;
using WinTabber.API;
using WinTabber.API.Suspension;
using WinTabber.API.Thumbnails;
using WinTabber.Events;
using WinTabberUI.Models.Settings;

namespace WinTabberUI.ViewModels;

public partial class WindowSelectorViewModel : ReactiveObject, IDisposable, IActivatableViewModel
{
    private WindowItem[] _windowItems = [];
    private WindowItem? _selectedItem;
    private int _selectedIndex = -1;

    public WindowSelectorViewModel(
        ApplicationStateViewModel applicationState,
        WinTabberEventManager eventManager,
        WindowManager windowManager,
        IProcessSuspensionService suspensionService,
        IWindowThumbnailService thumbnailService,
        ApplicationSettings settings)
    {
        _applicationState = applicationState ?? throw new ArgumentNullException(nameof(applicationState));
        WindowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _eventManager = eventManager;
        _suspensionService = suspensionService ?? throw new ArgumentNullException(nameof(suspensionService));
        _thumbnailService = thumbnailService ?? throw new ArgumentNullException(nameof(thumbnailService));
        _settings = settings.General;

        IsEditing = this.WhenAnyValue(vm => vm.WindowItems)
            .Select(items =>
            {
                if (items == null || items.Length == 0)
                    return Observable.Return(false);

                return items
                    .Select(item => item.WhenAnyValue(x => x.IsEditing).StartWith(false))
                    .CombineLatest()
                    .Select(states => states.Any(x => x));
            })
            .Switch()
            .DistinctUntilChanged();

        var scheduler = RxApp.MainThreadScheduler;

        var appChanges = _applicationState.ActiveApplicationChanges
            .Where(app => app is null)
            .ObserveOn(scheduler)
            .Subscribe(Clear);

        var winChanges = _applicationState.ActiveWindowChanges
            .Where(window => window is not null)
            .Select(window => window!.Process.Application.GetWindows())
            .ObserveOn(scheduler)
            .Subscribe(Update);

        var nextEvents = eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.CmdNextWindow)
            .ObserveOn(scheduler)
            .Subscribe(_ => SelectNext());

        var prevEvents = eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.CmdPreviousWindow)
            .ObserveOn(scheduler)
            .Subscribe(_ => SelectPrevious());

        // §5/D3: commit is now its own derived event rather than an overload of CmdAppHide. It is
        // emitted when the modifiers that *opened* the switcher are released — captured
        // per-activation, so a second binding's modifiers can never wedge the switcher open.
        var selectEvents = eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.CmdCommitSelection)
            .WithLatestFrom(IsSwitcherActiveChanges)
            .Where(state => state.Second)
            .ObserveOn(scheduler)
            .Subscribe(_ => SelectAndClose());

        _cleanUp = new CompositeDisposable(
            appChanges, winChanges, nextEvents, prevEvents, selectEvents
        );

        this.WhenActivated((x) =>
        {
            Disposable.Create(() =>
            {
                Debug.WriteLine("deactivated");
            }).DisposeWith(x);
        });
    }

    [Lazy]
    private IObservable<bool> GetIsSwitcherActiveChanges()
    {
        return _eventManager.CommandEvents
            .SubscribeOn(RxSchedulers.TaskpoolScheduler)
            .Where(evt => evt.Type.IsOneOf(EventType.CmdNextWindow, EventType.CmdPreviousWindow, EventType.CmdAppHide, EventType.CmdCommitSelection, EventType.WindowSelected))
            .WithLatestFrom<WinTabberEvent, bool, (WinTabberEvent CommandEvent, bool IsEditing)>(IsEditing, (command, isEditing) => (command, isEditing))
            .Select(evt =>
            {
                var command = evt.CommandEvent;
                var isEditing = evt.IsEditing;
                return command.Type switch
                {
                    EventType.CmdNextWindow => true,
                    EventType.CmdPreviousWindow => true,
                    EventType.WindowSelected => false,
                    EventType.CmdCommitSelection => false,
                    // CmdAppHide keeps its existing isEditing mapping. Now that ObserveKeyCommands
                    // is gone its only producer is App.xaml.cs (app exit/hide), and that path must
                    // still dismiss the switcher.
                    EventType.CmdAppHide => isEditing,
                    _ => throw new InvalidOperationException()
                };
            })
            .StartWith(false)
            .DistinctUntilChanged()
            .Replay(1)
            .RefCount()
            .ObserveOn(RxApp.MainThreadScheduler);
    }

    public IObservable<bool> IsEditing { get; }

    public WindowItem[] WindowItems
    {
        get => _windowItems;
        private set
        {
            new CompositeDisposable(_windowItems).Dispose();
            this.RaiseAndSetIfChanged(ref _windowItems, value);
        }
    }

    public WindowItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (value == _selectedItem) return;
            this.RaiseAndSetIfChanged(ref _selectedItem, value);
            _selectedIndex = _windowItems.IndexOf(value);
            this.RaisePropertyChanged(nameof(SelectedIndex));
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value == _selectedIndex) return;
            this.RaiseAndSetIfChanged(ref _selectedIndex, value);
            _selectedItem = _windowItems.ElementAtOrDefault(value);
            this.RaisePropertyChanged(nameof(SelectedItem));
        }
    }

    private void SelectPrevious()
    {
        if (WindowItems.Length == 0) return;
        var index = SelectedIndex - 1;
        SelectedIndex = index < 0 ? WindowItems.Length - 1 : index;
    }

    private void SelectNext()
    {
        if (WindowItems.Length == 0) return;

        // WindowItems is ordered most-recently-focused first, so index 0 is the window that
        // already has focus. On a fresh activation (SelectedIndex == -1) the first "next" must
        // therefore land on index 1 — the second-most-recently-focused window — not on 0.
        var index = SelectedIndex < 0 ? 1 : SelectedIndex + 1;
        SelectedIndex = index % WindowItems.Length;
    }

    private void SelectAndClose()
    {
        if (SelectedItem is not null && !SelectedItem.IsEditing)
        {
            SelectedItem.Activate();
            Deactivate();
            _eventManager.SendEvent(EventType.WindowSelected);
        }
    }

    public WindowManager WindowManager { get; }

    private readonly WinTabberEventManager _eventManager;

    private System.Drawing.Point Cursor => Control.MousePosition;

    public Screen CursorScreen => Screen.FromPoint(Cursor);

    public System.Drawing.Point CenterScreen => new System.Drawing.Point(CursorScreen.Bounds.X + CursorScreen.Bounds.Width / 2, CursorScreen.Bounds.Y + CursorScreen.Bounds.Height / 2);

    private void Clear(ApplicationRef? currentApplication)
    {
        Deactivate();
    }

    public void Update(IEnumerable<WindowRef> windows)
    {
        SelectedIndex = -1;
        WindowItems = windows
            .Select(w => new WindowItem(w, IsEditing.Select(x => !x), _suspensionService, _thumbnailService, _settings))
            .ToArray()
            ?? Array.Empty<WindowItem>();
    }

    /// <summary>
    /// Drop the selection, leaving the tiles in place.
    /// <para>
    /// Deliberately does <i>not</i> empty <see cref="WindowItems" />. Blanking the list here was
    /// the cause of the switcher sometimes opening empty: the only writer of WindowItems is a
    /// foreground-change event, and the close paths that activate no window (Esc, or a click
    /// landing with nothing selected) produce none. Normally the switcher window taking the
    /// foreground means closing it hands focus back and that refills the list, but Windows'
    /// foreground lock makes Activate() intermittent -- when it loses, open and close produce no
    /// foreground change at all and the emptied list stays empty until the next app switch.
    /// </para>
    /// <para>
    /// The tiles already describe the focused application, so leaving them in place is both
    /// correct and free.
    /// </para>
    /// </summary>
    internal void Deactivate()
    {
        SelectedIndex = -1;
    }

    /// <summary>
    /// Tell the rest of the app the switcher is no longer open.
    /// <para>
    /// This matters more than it used to: the commit tracker (§5) holds an active hold set until it
    /// sees the switcher close, and it only learns about closes through
    /// <see cref="EventType.WindowSelected" /> and <see cref="EventType.CmdAppHide" />. The
    /// window's own click-to-close and Esc paths bypass <c>SelectAndClose</c>, so without this they
    /// would leave the tracker armed and the *next* modifier release would fire a stray commit.
    /// </para>
    /// </summary>
    internal void NotifySwitcherClosed()
    {
        _eventManager.SendEvent(EventType.WindowSelected);
    }

    /// <summary>Dismiss the switcher without activating anything (§5 Esc fallback).</summary>
    internal void CancelSelection()
    {
        Deactivate();
        EndPreview();
        NotifySwitcherClosed();
    }

    /// <summary>Commit the current selection from the switcher window itself (§5 Enter fallback).</summary>
    internal void CommitSelection()
    {
        SelectAndClose();
    }

    private readonly ApplicationStateViewModel _applicationState;
    private readonly IProcessSuspensionService _suspensionService;
    private readonly IWindowThumbnailService _thumbnailService;
    private readonly GeneralSettings _settings;
    private readonly CompositeDisposable _cleanUp;

    public void PreviewSelectedWindow()
    {
        SelectedItem?.WindowRef.Preview(SelectedItem.Handle);
    }

    public void EndPreview()
    {
        WindowManager.EndPreview();
    }

    public void Dispose()
    {
        _cleanUp.Dispose();
    }

    public ViewModelActivator Activator { get; } = new ViewModelActivator();
}
