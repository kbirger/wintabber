using DynamicData;
using ReactiveUI;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows.Forms;
using WinTabber.Api.Windowing;
using WinTabber.Api.Windowing.Suspension;
using WinTabber.Api.Windowing.Thumbnails;
using WinTabber.Events;
using WinTabber.Events.Shortcuts;
using WinTabberUI.Models.Settings;
using WinTabberUI.Services;

namespace WinTabber.ViewModels;

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
            .Subscribe(state => SelectAndClose((state.First as WinTabberEvent<ShortcutModifiers>)?.Arg));

        var canCloseApplication = this.WhenAnyValue(vm => vm.WindowItems)
            .Select(items => items.Length > 0 && _settings.EnableCloseApplicationWindows);

        CloseApplicationCommand = ReactiveCommand.Create(CloseApplication, canCloseApplication);

        _cleanUp = new CompositeDisposable(
            appChanges, winChanges, nextEvents, prevEvents, selectEvents,
            CloseApplicationCommand
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
        RefreshOnActivation();

        if (WindowItems.Length == 0) return;
        var index = SelectedIndex - 1;
        SelectedIndex = index < 0 ? WindowItems.Length - 1 : index;
    }

    /// <summary>
    /// A negative <see cref="SelectedIndex" /> means no switcher session is in progress, so this
    /// command is opening one. That is the moment the tile list has to be correct -- both because it
    /// is about to be shown, and because the index the caller is about to compute is only meaningful
    /// against the right list. A repeat press mid-session must not refresh: the list is meant to hold
    /// still while the user cycles through it.
    /// </summary>
    private void RefreshOnActivation()
    {
        if (SelectedIndex < 0)
        {
            RefreshFromForeground();
        }
    }

    private void SelectNext()
    {
        RefreshOnActivation();

        if (WindowItems.Length == 0) return;

        // WindowItems is ordered most-recently-focused first, so index 0 is the window that
        // already has focus. On a fresh activation (SelectedIndex == -1) the first "next" must
        // therefore land on index 1 — the second-most-recently-focused window — not on 0.
        var index = SelectedIndex < 0 ? 1 : SelectedIndex + 1;
        SelectedIndex = index % WindowItems.Length;
    }

    private void SelectAndClose(ShortcutModifiers? heldModifiers = null)
    {
        if (SelectedItem is not null && !SelectedItem.IsEditing)
        {
            var selected = SelectedItem;
            selected.Activate();

            var modifiers = heldModifiers ?? _eventManager.HeldModifiers;
            if (
                _settings.EnableFocusSelect
                && _settings.FocusSelectModifier != ShortcutModifiers.None
                && modifiers.HasFlag(_settings.FocusSelectModifier)
            )
            {
                MinimizeOthers(selected);
            }

            Deactivate();
            _eventManager.SendEvent(EventType.WindowSelected);
        }
    }

    /// <summary>
    /// Minimizes everything except <paramref name="selected" />, per Focus Select's scope setting.
    /// Elevation-aware via <see cref="WindowManager.MinimizeWindows" /> — an elevated window can't
    /// be minimized by a direct call any more than it can be closed directly (UIPI).
    /// </summary>
    private void MinimizeOthers(WindowItem selected)
    {
        if (_settings.FocusSelectScope == FocusSelectScope.AllWindows)
        {
            // Same enumeration DockWindow already uses live in the UI, so it's proven fast enough
            // interactively; it also already excludes our own process's windows. A synthesized
            // Win+Home was tried here first, but the modifier that triggers Focus Select is by
            // definition still held when this runs, so the OS saw e.g. Ctrl+Win+Home and never
            // fired its own "minimize all but active" gesture.
            WindowManager.MinimizeWindows(
                WindowManager.GetWindows().Where(window => window.Handle != selected.Handle));
            return;
        }

        WindowManager.MinimizeWindows(WindowItems.Where(item => item != selected).Select(item => item.WindowRef));
    }

    private void CloseApplication()
    {
        // Re-checked live here (not just via the command's canExecute) because canExecute is
        // derived from WindowItems changes and can go stale the same way
        // IsCloseApplicationButtonVisible could — see that property's doc comment.
        if (!_settings.EnableCloseApplicationWindows || WindowItems.Length == 0)
        {
            return;
        }

        var application = WindowItems[0].WindowRef.Process.Application;
        application.CloseAllWindows(WindowItems.Select(item => item.WindowRef));

        CancelSelection();
    }

    public ReactiveCommand<Unit, Unit> CloseApplicationCommand { get; }

    /// <summary>
    /// Computed live rather than cached via a WhenAnyValue(vm => vm.WindowItems) derivation,
    /// because <see cref="Update" /> deliberately skips reassigning <see cref="WindowItems" />
    /// when the incoming window set is unchanged (<see cref="IsSameAsCurrent" />) — so a
    /// WindowItems-driven cache would stay stuck at whatever it was the last time the window set
    /// genuinely changed. Toggling <see cref="GeneralSettings.EnableCloseApplicationWindows" /> in
    /// Settings and reopening the switcher on the same application, with the same windows, would
    /// never re-evaluate it. <see cref="Update" /> raises <c>PropertyChanged</c> for this property
    /// unconditionally so WPF re-reads it on every switcher activation and every
    /// foreground-window-change notification, not just when the tile list itself changes.
    /// </summary>
    public bool IsCloseApplicationButtonVisible => WindowItems.Length > 0 && _settings.EnableCloseApplicationWindows;

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
        var incoming = windows as WindowRef[] ?? windows.ToArray();

        // Re-check on every call, including the early-return below, so a settings change picked
        // up between activations (see IsCloseApplicationButtonVisible's doc comment) is reflected
        // even when the window set itself hasn't changed.
        this.RaisePropertyChanged(nameof(IsCloseApplicationButtonVisible));

        // A notification describing the tiles we are already showing carries no information.
        // Rebuilding for it would throw away every WindowItem and construct a replacement set, which
        // clears the selection and makes the ListView regenerate every container -- re-registering
        // each tile's DWM thumbnail -- to arrive back where it started. This is now the common case:
        // RefreshFromForeground() rebuilds as the switcher opens, and the notification it raced
        // arrives a moment later saying the same thing.
        if (IsSameAsCurrent(incoming))
        {
            return;
        }

        SelectedIndex = -1;
        WindowItems = incoming
            .Select(w => new WindowItem(w, IsEditing.Select(x => !x), _suspensionService, _thumbnailService, _settings))
            .ToArray()
            ?? Array.Empty<WindowItem>();
    }

    /// <summary>
    /// Whether <paramref name="windows" /> describes exactly the tiles currently on display.
    /// <para>
    /// Compares titles as well as handles and order, so this stays a pure redundancy check: a
    /// <see cref="WindowItem" /> captures its title at construction, so a window that has been
    /// renamed since must still force a rebuild even though the handles line up.
    /// </para>
    /// </summary>
    private bool IsSameAsCurrent(WindowRef[] windows)
    {
        if (windows.Length != _windowItems.Length)
        {
            return false;
        }

        for (var i = 0; i < windows.Length; i++)
        {
            if (windows[i].Handle != _windowItems[i].Handle || windows[i].Title != _windowItems[i].Title)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Rebuild <see cref="WindowItems" /> from the live foreground window, synchronously, as a
    /// switcher session opens. Leaves the selection alone -- the caller owns that.
    /// <para>
    /// Tile order comes from <see cref="WindowManager" />'s activation history, and that history only
    /// advances when a foreground-change notification is *delivered*. Delivery is marshalled twice --
    /// the WinEvent hook raises on its own scheduler thread, <c>ActiveWindowStateService</c> posts to
    /// the dispatcher, and this view model posts again -- while the hotkey that opens the switcher
    /// reaches the same dispatcher by a shorter path. Open the switcher immediately after switching
    /// windows and the hotkey wins that race: the tiles paint in the *previous* order, and the queued
    /// rebuild lands a few tens of ms later and visibly reorders them in front of the user. (The
    /// thumbnails look unaffected only because the discarded pass's DWM thumbnails never became
    /// visible.) The stale list also corrupts the selection, since the index the caller computes is
    /// taken modulo a length that may not even be right.
    /// </para>
    /// <para>
    /// Reading the foreground directly here removes the race rather than narrowing it: what the
    /// switcher shows becomes a function of the state at the moment it opens, not of whichever
    /// notification happened to have been delivered by then.
    /// </para>
    /// </summary>
    private void RefreshFromForeground()
    {
        if (WindowManager.CurrentWindow() is not { } window)
        {
            // Nothing resolvable in the foreground. Leave the tiles in place rather than blanking
            // them, for the reason spelled out in Deactivate()'s remarks.
            return;
        }

        // Our own switcher window can still hold the foreground across a close/open pair. Rebuilding
        // from it would ask ApplicationRef for this process's own windows, which it deliberately
        // excludes, and empty the switcher.
        if (!window.Process.Application.IsValidProcess)
        {
            return;
        }

        // The history is advanced from the hook thread on delivery -- which is precisely what may not
        // have happened yet. Record what is actually in front right now so the ordering below
        // reflects it.
        WindowManager.RegisterForegroundWindowChanged(window.Handle);

        var windows = window.Process.Application.GetWindows();
        if (windows.Length == 0)
        {
            return;
        }

        Update(windows);
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
