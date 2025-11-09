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

    internal DockWindow? _dock;
    internal MediaControlsWindow? _mediaWindow;
    private WinTabberWindowCoordinator? _windowCoordinator;
    private WindowCommandCoordinator? _commandCoordinator;
    private ApplicationStateMonitor? _appState;
    private WinTabberEventManagerThreadHost? _eventManager;

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
            .AddSingleton<WinTabberEventManagerThreadHost>()
            .AddSingleton<WinTabberEventManager>()
            .AddSingleton<ApplicationStateMonitor>()
            .AddSingleton<ApplicationState>()
            .AddSingleton<IInteropProxy, InteropProxy>()
            .AddSingleton<WindowManager>()
            .AddSingleton<WinTabberWindowCoordinator>()
            .AddSingleton<WindowCommandCoordinator>()
            .AddTransient<DockWindow>()
            .AddTransient<MediaControlsWindow>()
            .AddSingleton<MainWindow>()
            .AddSingleton<DockWindowViewModel>()
            .AddSingleton<WindowSelectorViewModel>()
            .AddSingleton<MediaControlsViewModel>()
            .AddTransient<WindowRenameViewModel>()
            .BuildServiceProvider();

        Ioc.Default.ConfigureServices(serviceProvider);
        Ioc.Default.GetRequiredService<MainWindow>();
        _windowCoordinator = Ioc.Default.GetRequiredService<WinTabberWindowCoordinator>();
        _commandCoordinator = Ioc.Default.GetRequiredService<WindowCommandCoordinator>();
        _appState = Ioc.Default.GetRequiredService<ApplicationStateMonitor>();
        var wm = Ioc.Default.GetRequiredService<WindowManager>();
        _eventManager = Ioc.Default.GetRequiredService<WinTabberEventManagerThreadHost>();

        
        var currentProcess = Process.GetCurrentProcess().ProcessName;
        _appState.ActiveWindowChanges
            .Where(window => window is not null)
            .Subscribe(window => wm.RegisterForegroundWindowChanged(window!.Handle));

        // _eventManager.ApplicationChange
        //     .ObserveOnDispatcher()
        //     .Where(evt => evt.Arg != currentProcess)
        //     .Subscribe(evt =>
        //     {
        //         if (_dock is null)
        //         {
        //             return;
        //         }
        //         if (_dock.ApplicationName != evt.Arg)
        //         {
        //             _dock.ApplicationName = evt.Arg;
        //         }
        //     });

        // _eventManager.CommandEvents
        //     .ObserveOnDispatcher()
        //     .Where(evt => evt.Type == EventType.CmdDockWindow)
        //     .Subscribe(evt => ToggleWindow(ref _dock, () => new DockWindow() { ApplicationName = wm.GetCurrentApplication()?.ProcessName }, () => _dock = null));

        base.OnStartup(e);
    }

    private void ToggleWindow<T>(ref T? window, Func<T> create, Action unset) where T : Window
    {
        if (window is null)
        {
            window = create();
            window.Closed += (_, _) => unset();

            window.Show();
        }
        else
        {
            window?.Close();
            unset();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _commandCoordinator?.Dispose();
        _windowCoordinator?.Dispose();
        _eventManager?.Dispose();
    }
}
