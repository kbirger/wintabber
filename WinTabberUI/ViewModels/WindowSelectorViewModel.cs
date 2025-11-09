using DynamicData;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using WinTabber.API;
using WinTabber.Events;
using WinTabber.Interop;
using WinTabberUI.Models;

namespace WinTabberUI.ViewModels;

public class WindowSelectorViewModel : DependencyObject
{
    private static bool EventOneOf(EventType type, params EventType[] types)
    {
        return types.Contains(type);
    }
    public WindowSelectorViewModel(ApplicationStateMonitor applicationState, WinTabberEventManager eventManager, WindowManager windowManager)
    {
        _applicationState = applicationState ?? throw new ArgumentNullException(nameof(applicationState));
        WindowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _eventManager = eventManager;
        _applicationState.ActiveApplicationChanges
            .Where(app => app is null)
            .ObserveOnDispatcher()
            .Subscribe(Clear);

        _applicationState.ActiveApplicationChanges
            .Where(app => app is not null)
            .Select(GetWindows)
            .ObserveOnDispatcher()
            .Subscribe(Activate);

        eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.CmdNextWindow)
            .ObserveOnDispatcher()
            .Subscribe(_ => SelectedIndex++);

        eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.CmdPreviousWindow)
            .ObserveOnDispatcher()
            .Subscribe(_ => SelectedIndex--);

        eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.CmdAppHide)
            .ObserveOnDispatcher()
            .Subscribe(SelectAndClose);
    }

    private void SelectAndClose(WinTabberEvent evt)
    {
        if(SelectedItem is not null && !SelectedItem.IsEditing)
        {
            SelectedItem.Activate();
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
        WindowItems = [];
    }

    private List<WindowRef> GetWindows(ApplicationRef? application)
    {
        return application!.GetWindows().ToList();
    }
    public void Activate(List<WindowRef> windows)
    {
        //var currentApplication = _applicationState.ActiveApplication;

        //if (currentApplication is null)
        //{
        //    WindowItems.Clear();
        //    return;
        //}

        //var windows = currentApplication.GetWindows2().ToList();
        SelectedIndex = -1;
        //WindowItems.Clear();
        WindowItems = windows
            .Select(w => new WindowItem(w))
            .ToArray()
            ?? Array.Empty<WindowItem>();

        if (windows.Count > 0)
        {
            SelectedIndex = 0;
        }
    }

    internal void Deactivate()
    {
        WindowItems = [];
        SelectedItem = null;
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
        new PropertyMetadata(null));
    private DependencyProperty _selectedIndex = DependencyProperty.Register(
        "SelectedIndex",
        typeof(int),
        typeof(WindowSelectorViewModel),
        new PropertyMetadata(-1));

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
                SetValue(_selectedIndex, WindowItems.IndexOf(value));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedItem)));
            }
        }
    }
    public int SelectedIndex
    {
        get { return (int)GetValue(_selectedIndex); }
        set
        {
            if (value >= WindowItems.Length)
            {
                value = 0;
            }
            else if (value < 0)
            {
                value = WindowItems.Length - 1;
            }

            if(value != SelectedIndex)
            {
                SetValue(_selectedIndex, value);
                SetValue(_selectedItem, WindowItems.ElementAtOrDefault(value));
            }

        }
    }
}
