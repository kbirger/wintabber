using DynamicData;
using ReactiveUI;
using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Forms;
using WinTabber.API;
using WinTabber.Events;
using WinTabberUI.Extensions;
using WinTabberUI.Models;

namespace WinTabberUI.ViewModels;

public partial class WindowSelectorViewModel : DependencyObject, IDisposable, IActivatableViewModel
{
    public WindowSelectorViewModel(ApplicationStateViewModel applicationState, WinTabberEventManager eventManager, WindowManager windowManager)
    {
        _applicationState = applicationState ?? throw new ArgumentNullException(nameof(applicationState));
        WindowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _eventManager = eventManager;

        //IsEditing = this.WhenAny(vm => vm.SelectedItem!.IsEditing, (x) => x.Value);
        IsEditing = this.WhenAnyValue(vm => vm.WindowItems)
            .Select(items =>
            {
                if (items == null || items.Length == 0)
                    return Observable.Return(false);

                // Combine all item IsEditing streams into one that emits true if any are true
                return items
                    .Select(item => item.WhenAnyValue(x => x.IsEditing).StartWith(false))
                    .CombineLatest()
                    .Select(states => states.Any(x => x));
            })
            .Switch() // switch to the latest combined stream when items list changes
            .DistinctUntilChanged();



        var appChanges = _applicationState.ActiveApplicationChanges
            .Where(app => app is null)
            .ObserveOnDispatcher()
            .Subscribe(Clear);

        var winChanges = _applicationState.ActiveWindowChanges
            .Where(window => window is not null)
            .Select(window => window!.Process.Application.GetWindows())
            .ObserveOnDispatcher()
            .Subscribe(Update);

        var nextEvents = eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.CmdNextWindow)
            .ObserveOnDispatcher()
            .Subscribe(_ => SelectNext());

        var prevEvents = eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.CmdPreviousWindow)
            .ObserveOnDispatcher()
            .Subscribe(_ => SelectPrevious());

        var selectEvents = eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.CmdAppHide)
            .WithLatestFrom(IsSwitcherActiveChanges)
            .Where(state => state.Second)
            .ObserveOnDispatcher()
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
            .SubscribeOn(RxApp.TaskpoolScheduler)
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
            .ObserveOnDispatcher();
    }

    public IObservable<bool> IsEditing { get; }
    private void SelectPrevious()
    {
        var index = SelectedIndex - 1;
        if (index < 0)
        {
            SelectedIndex = WindowItems.Length - 1;
        }
        else
        {
            SelectedIndex = index;
        }
    }

    private void SelectNext()
    {
        if(WindowItems.Length == 0)
        {
            return;
        }
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

    private List<WindowRef> GetWindows(ApplicationRef? application)
    {
        return application!.GetWindows().ToList();
    }
    public void Update(IEnumerable<WindowRef> windows)
    {
        //var currentApplication = _applicationState.ActiveApplication;

        //if (currentApplication is null)
        //{
        //    WindowItems.Clear();
        //    return;
        //}
        //var windows = currentApplication.GetWindows2().ToList();
        SelectedIndex = -1;
        // SelectedItem = null;
        //WindowItems.Clear();
        WindowItems = windows
            .Select(w => new WindowItem(w, IsEditing.Select(x => !x)))
            .ToArray()
            ?? Array.Empty<WindowItem>();

        // if (windows.Count > 0)
        // {
        //     SelectedIndex = 0;
        // }
    }

    internal void Deactivate()
    {
        WindowItems = [];
        // SelectedItem = null;
        SelectedIndex = -1;
    }

    public WindowItem[] WindowItems
    {
        get
        {
            return (WindowItem[])GetValue(WindowItemsProperty);
        }

        private set
        {
            new CompositeDisposable(WindowItems).Dispose();
            SetValue(WindowItemsProperty, value);
        }
    }



    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        "SelectedItem",
        typeof(WindowItem),
        typeof(WindowSelectorViewModel),
        new PropertyMetadata(null, OnSelectedItemChanged));

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if(e.OldValue != e.NewValue && d is WindowSelectorViewModel vm)
        {
            vm.SelectedIndex = vm.WindowItems.IndexOf(vm.SelectedItem);
        }
    }

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        "SelectedIndex",
        typeof(int),
        typeof(WindowSelectorViewModel),
        new PropertyMetadata(-1, OnSelectedIndexChanged));

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if(e.OldValue != e.NewValue && d is WindowSelectorViewModel vm)
        {
            vm.SelectedItem = vm.WindowItems.ElementAtOrDefault(vm.SelectedIndex);
        }
    }

    public static readonly DependencyProperty WindowItemsProperty = DependencyProperty.Register(
        "WindowItems",
        typeof(WindowItem[]),
        typeof(WindowSelectorViewModel),
        new PropertyMetadata(Array.Empty<WindowItem>()));

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

    public WindowItem? SelectedItem
    {
        get { return (WindowItem)GetValue(SelectedItemProperty); }
        set
        {
            if (value != SelectedItem)
            {
                SetValue(SelectedItemProperty, value);
            }
        }
    }
    public int SelectedIndex
    {
        get { return (int)GetValue(SelectedIndexProperty); }
        set
        {
            if (value != SelectedIndex)
            {
                SetValue(SelectedIndexProperty, value);
            }
        }
    }

    public ViewModelActivator Activator { get; } = new ViewModelActivator();
}
