using DynamicData;
using ReactiveUI;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows.Forms;
using WinTabber.API;
using WinTabber.Events;

namespace WinTabberUI.ViewModels;

public partial class WindowSelectorViewModel : ReactiveObject, IDisposable, IActivatableViewModel
{
    private WindowItem[] _windowItems = [];
    private WindowItem? _selectedItem;
    private int _selectedIndex = -1;

    public WindowSelectorViewModel(ApplicationStateViewModel applicationState, WinTabberEventManager eventManager, WindowManager windowManager)
    {
        _applicationState = applicationState ?? throw new ArgumentNullException(nameof(applicationState));
        WindowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _eventManager = eventManager;

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

        var selectEvents = eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.CmdAppHide)
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
            .Where(evt => evt.Type.IsOneOf(EventType.CmdNextWindow, EventType.CmdPreviousWindow, EventType.CmdAppHide, EventType.WindowSelected))
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
        var index = SelectedIndex - 1;
        SelectedIndex = index < 0 ? WindowItems.Length - 1 : index;
    }

    private void SelectNext()
    {
        if (WindowItems.Length == 0) return;
        SelectedIndex = (SelectedIndex + 1) % WindowItems.Length;
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
            .Select(w => new WindowItem(w, IsEditing.Select(x => !x)))
            .ToArray()
            ?? Array.Empty<WindowItem>();
    }

    internal void Deactivate()
    {
        WindowItems = [];
        SelectedIndex = -1;
    }

    private readonly ApplicationStateViewModel _applicationState;
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
