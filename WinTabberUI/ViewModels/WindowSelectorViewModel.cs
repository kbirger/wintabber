using DynamicData;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Forms;
using WinTabber.API;
using WinTabber.Events;
using WinTabberUI.Models;

namespace WinTabberUI.ViewModels;

public class WindowSelectorViewModel : DependencyObject
{
    public WindowSelectorViewModel(ApplicationStateMonitor applicationState, WinTabberEventManager eventManager, WindowManager windowManager)
    {
        _applicationState = applicationState ?? throw new ArgumentNullException(nameof(applicationState));
        WindowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _eventManager = eventManager;
        _applicationState.ActiveApplicationChanges
            .Where(app => app is null)
            .ObserveOnDispatcher()
            .Subscribe(Clear);

        _applicationState.ActiveWindowChanges
            .Where(window => window is not null)
            .Select(window => window!.Process.Application.GetWindows())
            .ObserveOnDispatcher()
            .Subscribe(Update);

        eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.CmdNextWindow)
            .ObserveOnDispatcher()
            .Subscribe(_ => SelectNext());

        eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.CmdPreviousWindow)
            .ObserveOnDispatcher()
            .Subscribe(_ => SelectPrevious());

        eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.CmdAppHide)
            .WithLatestFrom(applicationState.IsSwitcherActiveChanges)
            .Where(state => state.Second)
            .ObserveOnDispatcher()
            .Subscribe(_ => SelectAndClose());
    }

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
            .Select(w => new WindowItem(w))
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
            return (WindowItem[])GetValue(_windowItems);
        }

        private set
        {
            SetValue(_windowItems, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WindowItems)));
        }
    }



    private DependencyProperty _selectedItem = DependencyProperty.Register(
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

    private DependencyProperty _selectedIndex = DependencyProperty.Register(
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

    private DependencyProperty _windowItems = DependencyProperty.Register(
        "WindowItems",
        typeof(WindowItem[]),
        typeof(WindowSelectorViewModel),
        new PropertyMetadata(Array.Empty<WindowItem>()));

    private readonly ApplicationStateMonitor _applicationState;

    public event PropertyChangedEventHandler? PropertyChanged;


    public void PreviewSelectedWindow()
    {
        SelectedItem?.WindowRef.Preview(SelectedItem.Handle);
    }

    public void EndPreview()
    {
        WindowManager.EndPreview();
    }

    public WindowItem? SelectedItem
    {
        get { return (WindowItem)GetValue(_selectedItem); }
        set
        {
            if (value != SelectedItem)
            {
                SetValue(_selectedItem, value);
            }
        }
    }
    public int SelectedIndex
    {
        get { return (int)GetValue(_selectedIndex); }
        set
        {
            if (value != SelectedIndex)
            {
                SetValue(_selectedIndex, value);
            }
        }
    }
}
