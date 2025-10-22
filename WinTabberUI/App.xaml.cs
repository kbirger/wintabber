using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Timers;
using System.Windows;
using WinTabber.API;
using WinTabber.Events;
using WinTabber.Interop;
using WinTabberUI.Coordinators;
using WinTabberUI.Models;
using WinTabberUI.ViewModels;
using WinTabberUI.Windowing;

namespace WinTabberUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    //System.Timers.Timer _timer = new(1000);

    protected override void OnActivated(EventArgs e)
    {
        var area = DesktopHelper.GetDesktopArea();
        base.OnActivated(e);

    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        //WinTabberEventManagerThreadHost.Instance.SendEvent(EventType.AppHide);
    }

    protected override void OnStartup(StartupEventArgs e)
    {

        var serviceProvider = new ServiceCollection()
            .AddSingleton<EventMonitor>()
            .AddSingleton<ApplicationState>()
            .AddSingleton<IInteropProxy, InteropProxy>()
            .AddSingleton<WindowManager>()
            .AddSingleton<WinTabberWindowCoordinator>()
            .AddTransient<DockWindow>()
            .AddTransient<MediaControlsWindow>()
            .AddSingleton<MainWindow>()
            .AddSingleton<DockWindowViewModel>()
            .AddSingleton<WindowsViewModel>()
            .AddTransient<WindowRenameViewModel>()
            .BuildServiceProvider();

        Ioc.Default.ConfigureServices(serviceProvider);
        var windowCoordinator = Ioc.Default.GetRequiredService<WinTabberWindowCoordinator>();
        var eventMonitor = Ioc.Default.GetRequiredService<EventMonitor>();
       
        //var mgr = WinTabberEventManagerThreadHost.Instance;


        

        //mgr.ApplicationChange
        //    .ObserveOn(SynchronizationContext.Current)
        //    .Where(evt => evt.Arg != currentProcess)
        //    .Subscribe(evt =>
        //    {
        //        if (_dock is null)
        //        {
        //            return;
        //        }
        //        if (_dock.ApplicationName != evt.Arg)
        //        {
        //            _dock.ApplicationName = evt.Arg;
        //        }
        //    });

        //mgr.WindowChange
        //    .Subscribe(e =>
        //    {
        //        wm.RegisterForegroundWindowChanged(e.Arg);
        //    });

        //mgr.CommandEvents
        //    .ObserveOn(SynchronizationContext.Current)
        //    .Where(evt => evt.Type == EventType.DockWindow)
        //    .Subscribe(evt => ToggleWindow(ref _dock, () => new DockWindow() { ApplicationName = wm.GetCurrentApplication()?.ProcessName }, () => _dock = null));

        //mgr.CommandEvents
        //    .ObserveOn(SynchronizationContext.Current)
        //    .Where(evt => evt.Type == EventType.MediaWindow)
        //    .Subscribe(evt => ToggleWindow(ref _mediaWindow, () => new MediaControlsWindow(), () => _mediaWindow = null));
        base.OnStartup(e);
    }

    //private void ToggleWindow<T>(ref T? window, Func<T> create, Action unset) where T: Window
    //{
    //    if (window is null)
    //    {
    //        window = create();
    //        window.Closed += (_, _) => unset();

    //        window.Show();
    //    }
    //    else
    //    {
    //        window?.Close();
    //        unset();
    //    }
    //}

    //private void _timer_Elapsed(object? sender, ElapsedEventArgs e)
    //{
    //    _dock.Dispatcher.Invoke(() =>
    //    {
    //        var app = wm.GetCurrentApplication();
    //        if (app is null)
    //        {
    //            return;
    //        }

    //        if (app.ProcessName == Process.GetCurrentProcess().ProcessName)
    //        {
    //            return;
    //        }
    //        _dock.ApplicationName = app.ProcessName;
    //    });

    //}

    protected override void OnExit(ExitEventArgs e)
    {
        //_hook?.Dispose();
    }
}
