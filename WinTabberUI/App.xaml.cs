
using CommunityToolkit.Mvvm.DependencyInjection;
using H.NotifyIcon;
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
using WinTabberUI.Services;
using WinTabberUI.Updaters;
using WinTabberUI.ViewModels;
using WinTabberUI.Views;
using WinTabberUI.Windowing;

namespace WinTabberUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{

    private WindowCommandCoordinator? _commandCoordinator;
    private WinTabberEventManager? _eventManager;
    private TaskbarIcon? notifyIcon;
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
        notifyIcon = (TaskbarIcon)FindResource("NotifyIcon");
        notifyIcon.ForceCreate();
        ServiceProvider serviceProvider = ConfigureServices();

        Ioc ioc = Ioc.Default;
        ioc.ConfigureServices(serviceProvider);
        ioc.GetRequiredService<WindowSelectorWindow>();
        //_windowCoordinator = Ioc.Default.GetRequiredService<WinTabberWindowCoordinator>();
        ioc.GetRequiredService<WindowSelectorViewCoordinator>().Init();
        ioc.GetRequiredService<MediaWindowViewCoordinator>().Init();

        ioc.GetRequiredService<WindowHistoryUpdater>().Init();


        _commandCoordinator = ioc.GetRequiredService<WindowCommandCoordinator>();
        ioc.GetRequiredService<ApplicationStateViewModel>();
        ioc.GetRequiredService<WindowManager>();
        _eventManager = ioc.GetRequiredService<WinTabberEventManager>();

        base.OnStartup(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IActiveWindowStateService, ActiveWindowStateService>()
            //.AddSingleton<IWindowSelectorStateService, WindowSelectorStateService>()
            .AddSingleton<IMediaControlsStateService, MediaControlsStateService>()
            .AddSingleton<ApplicationStateViewModelFactory>()
            .AddSingleton<ApplicationStateViewModel>((sp) =>
            {
                var factory = sp.GetRequiredService<ApplicationStateViewModelFactory>();
                return factory.CreateApplicationStateViewModel();
            })
            .AddSingleton<WinTabberEventManager>()
            .AddSingleton<ApplicationState>()
            .AddSingleton<IInteropProxy, InteropProxy>()
            .AddSingleton<WindowManager>()
            .AddSingleton<WindowHistoryUpdater>()

            .AddSingleton<WindowSelectorViewCoordinator>()
            .AddSingleton<MediaWindowViewCoordinator>()

            .AddSingleton<WindowCommandCoordinator>()
            .AddTransient<DockWindow>()
            .AddTransient<MediaControlsWindow>()
            .AddSingleton<WindowSelectorWindowFactory>()
            .AddSingleton(sp => sp.GetRequiredService<WindowSelectorWindowFactory>().CreateWindowSelectorWindow())
            .AddSingleton<DockWindowViewModel>()
            .AddSingleton<WindowSelectorViewModel>()
            .AddSingleton<MediaControlsViewModel>()
            .AddTransient<WindowRenameViewModel>()
            .BuildServiceProvider();
        return serviceProvider;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _commandCoordinator?.Dispose();
        _eventManager?.Dispose();
        notifyIcon?.Dispose();
    }
}
