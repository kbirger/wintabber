using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using WinTabber.API;
using WinTabber.Interop;
using WinTabberUI.Models;

namespace WinTabberUI.ViewModels;

public class WindowsViewModel(EventMonitor applicationState, WindowManager windowManager) : DependencyObject
{
    public WindowManager WindowManager { get; } = windowManager;

    private System.Drawing.Point Cursor => Control.MousePosition;

    public Screen CursorScreen => Screen.FromPoint(Cursor);

    public System.Drawing.Point CenterScreen => new System.Drawing.Point(CursorScreen.Bounds.X + CursorScreen.Bounds.Width / 2, CursorScreen.Bounds.Y + CursorScreen.Bounds.Height / 2);

    public void Activate()
    {
        var currentApplication = WindowManager.GetCurrentApplication();

        if (currentApplication is null)
        {
            WindowItems = [];
            return;
        }

        var windows = currentApplication.GetWindows2().ToList();
        SelectedIndex = -1;
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

        set
        {
            SetValue(_windowItems, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WindowItems)));
        }
    }



    private DependencyProperty _selectedItem = DependencyProperty.Register(
        "SelectedItem",
        typeof(WindowItem),
        typeof(WindowsViewModel),
        new PropertyMetadata(null));
    private DependencyProperty _selectedIndex = DependencyProperty.Register(
        "SelectedIndex",
        typeof(int),
        typeof(WindowsViewModel),
        new PropertyMetadata(-1));

    private DependencyProperty _windowItems = DependencyProperty.Register(
        "WindowItems",
        typeof(WindowItem[]),
        typeof(WindowsViewModel),
        new PropertyMetadata(Array.Empty<WindowItem>()));
    
    private readonly EventMonitor _applicationState = applicationState;

    public event PropertyChangedEventHandler? PropertyChanged;


    public void PreviewSelectedWindow()
    {
        SelectedItem?.WindowRef.Preview(SelectedItem.Handle);
    }

    public void EndPreview()
    {
        WindowManager.EndPreview();
    }

    public WindowItem SelectedItem
    {
        get { return (WindowItem)GetValue(_selectedItem); }
        set
        {
            if (value != SelectedItem)
            {
                SetValue(_selectedItem, value);
                SetValue(_selectedIndex, Array.IndexOf(WindowItems, value));
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
