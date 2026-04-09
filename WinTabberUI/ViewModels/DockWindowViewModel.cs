using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows;
using WinTabber.API;
using WinTabber.Interop;

namespace WinTabberUI.ViewModels;

public class DockWindowViewModel : DependencyObject
{

    public DockWindowViewModel(WindowManager windowManager)
    {
        _windowManager = windowManager;
    }


    private WindowManager _windowManager;
    private ApplicationRef? _application;

    private static DependencyProperty ApplicationNameProperty = DependencyProperty.Register(
        "ApplicationName",
        typeof(string),
        typeof(DockWindowViewModel),
        new PropertyMetadata(null, OnApplicationNameChanged));

    private static void OnApplicationNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if(d is DockWindowViewModel vm && e.NewValue is string newApplicationName)
        {
            vm.UpdateApplication(newApplicationName);
        }
    }

    private void UpdateApplication(string newApplicationName)
    {
        _application = new ApplicationRef(newApplicationName, _windowManager);
        RefreshWindows();
    }

    private void RefreshWindows()
    {
        if(_application is null)
        {
            return;
        }
        var windows = _application.GetWindows();
        Windows.Clear();
        foreach (var window in windows)
        {
            Windows.Add(new WindowItem(window, Observable.Empty<bool>()));
        }
    }

    public string ApplicationName
    {
        get { return (string)GetValue(ApplicationNameProperty); }
        set
        {
            SetValue(ApplicationNameProperty, value);
        }
    }

    public static DependencyProperty WindowsProperty = DependencyProperty.Register(
        "Windows",
        typeof(ObservableCollection<WindowItem>),
        typeof(DockWindowViewModel),
        new PropertyMetadata(new ObservableCollection<WindowItem>()));
    
    private readonly System.Timers.Timer _timer;

    public ObservableCollection<WindowItem> Windows
    {
        get { return (ObservableCollection<WindowItem>)GetValue(WindowsProperty); }
        set
        {
            SetValue(WindowsProperty, value);
        }
    }

    public WindowRef[] GetMaximizedWindows()
    {
        return _application
            ?.GetWindows()
            .Where(w => w.State == WindowPlacement.WindowState.Maximized)
            .ToArray() ?? [];
    }
}
