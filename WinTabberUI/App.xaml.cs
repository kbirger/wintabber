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
using WinTabberUI.Updaters;
using WinTabberUI.ViewModels;
using WinTabberUI.Windowing;

namespace WinTabberUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{

    private WindowCommandCoordinator? _commandCoordinator;
    private WinTabberEventManager? _eventManager;

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);

    }

    protected override void OnDeactivated(EventArgs e)
    {
        _eventManager?.SendEvent(EventType.CmdAppHide);
        base.OnDeactivated(e);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        ServiceProvider serviceProvider = ConfigureServices();

        Ioc ioc = Ioc.Default;
        ioc.ConfigureServices(serviceProvider);
        ioc.GetRequiredService<MainWindow>();
        //_windowCoordinator = Ioc.Default.GetRequiredService<WinTabberWindowCoordinator>();
        ioc.GetRequiredService<WindowSelectorViewCoordinator>().Init();
        ioc.GetRequiredService<MediaWindowViewCoordinator>().Init();

        ioc.GetRequiredService<WindowHistoryUpdater>().Init();


        _commandCoordinator = ioc.GetRequiredService<WindowCommandCoordinator>();
        ioc.GetRequiredService<ApplicationStateMonitor>();
        ioc.GetRequiredService<WindowManager>();
        _eventManager = ioc.GetRequiredService<WinTabberEventManager>();


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

    private static ServiceProvider ConfigureServices()
    {
        var serviceProvider = new ServiceCollection()
            .AddSingleton<WinTabberEventManager>()
            .AddSingleton<ApplicationStateMonitor>()
            .AddSingleton<ApplicationState>()
            .AddSingleton<IInteropProxy, InteropProxy>()
            .AddSingleton<WindowManager>()
            .AddSingleton<WindowHistoryUpdater>()
            //.AddSingleton<WinTabberWindowCoordinator>()
            .AddSingleton<WindowSelectorViewCoordinator>()
            .AddSingleton<MediaWindowViewCoordinator>()
            .AddSingleton<ApplicationStateViewModel>((sp) =>
            {
                var state = sp.GetRequiredService<ApplicationStateMonitor>();
                return new ApplicationStateViewModel
                {
                    IsSwitcherActiveChanges = state.IsSwitcherActiveChanges,
                    ActiveApplicationChanges = state.ActiveApplicationChanges,
                    ActiveWindowChanges = state.ActiveWindowChanges,
                    IsDockActiveChanges = state.IsDockActiveChanges,
                    IsEditingStateChanges = state.IsEditingStateChanges,
                    IsMediaControlsActiveChanges = state.IsMediaControlsActiveChanges
                };
            })
            .AddSingleton<WindowCommandCoordinator>()
            .AddTransient<DockWindow>()
            .AddTransient<MediaControlsWindow>()
            .AddSingleton<MainWindow>()
            .AddSingleton<DockWindowViewModel>()
            .AddSingleton<WindowSelectorViewModel>()
            .AddSingleton<MediaControlsViewModel>()
            .AddTransient<WindowRenameViewModel>()
            .BuildServiceProvider();
        return serviceProvider;
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
        _eventManager?.Dispose();
    }
}
