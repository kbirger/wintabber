using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms.VisualStyles;
using WinTabber.API;
using WinTabber.Events;
using WinTabber.Interop;
using WinTabberUI.Coordinators;
using WinTabberUI.Models;
using WinTabberUI.Services;
using WinTabberUI.Updaters;
using WinTabberUI.ViewModels;
using WinTabberUI.Views;

namespace WinTabberUI;

public static class Bootstrapper
{
    public static IServiceProvider Init(Application application)
    {
        var serviceProvider = ConfigureServices(application);
        Ioc ioc = Ioc.Default;
        ioc.ConfigureServices(serviceProvider);

        return serviceProvider;
    }

    private static ServiceProvider ConfigureServices(Application application)
    {
        var serviceProvider = new ServiceCollection()
            .RegisterApplication(application)
            .AddCoordinators()
            .AddCoreServices()
            .AddDomainModels()
            .AddStateServices()
            .AddUpdaters()
            .AddViews()
            .AddViewModels()
            .BuildServiceProvider();
        return serviceProvider;
    }

    private static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        return services.AddSingleton<BackgroundServiceContainer>();
    }
    private static IServiceCollection AddDomainModels(this IServiceCollection services)
    {
        return services
            .AddSingleton<WinTabberEventManager>()
            .AddSingleton<ApplicationState>()
            .AddSingleton<IInteropProxy, InteropProxy>()
            .AddSingleton<WindowManager>();
    }
    private static IServiceCollection AddUpdaters(this IServiceCollection services)
    {
        return services
            .AddSingleton<WindowHistoryUpdater>();
    }

    private static IServiceCollection AddCoordinators(this IServiceCollection services)
    {
        return services
            .AddSingleton<WindowSelectorViewCoordinator>()
            .AddSingleton<SettingsWindowViewCoordinator>()
            .AddSingleton<MediaWindowViewCoordinator>()
            .AddSingleton<WindowCommandCoordinator>()
            .AddSingleton<NotifyIconCoordinator>();

    }

    private static IServiceCollection AddStateServices(this IServiceCollection services)
    {
        return services
            .AddSingleton<IActiveWindowStateService, ActiveWindowStateService>()
            .AddSingleton<IMediaControlsStateService, MediaControlsStateService>();
    }

    private static IServiceCollection RegisterApplication(this IServiceCollection services, Application application)
    {
        return services
            .AddSingleton(application);
    }

    private static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        return services
            .AddSingleton<DockWindowViewModel>()
            .AddSingleton<WindowSelectorViewModel>()
            .AddSingleton<MediaControlsViewModel>()
            .AddTransient<WindowRenameViewModel>()
            .AddSingleton<SettingsViewModel>()
            .AddSingleton<NotifyIconViewModel>();
    }
    private static IServiceCollection AddViews(this IServiceCollection services)
    {
        return services
            .AddSingleton<ApplicationStateViewModelFactory>()
            .AddSingleton((sp) =>
            {
                var factory = sp.GetRequiredService<ApplicationStateViewModelFactory>();
                return factory.CreateApplicationStateViewModel();
            })
            .AddTransient<DockWindow>()
            .AddTransient<SettingsWindow>()
            .AddTransient<MediaControlsWindow>()
            .AddSingleton<WindowSelectorWindowFactory>()
            .AddSingleton(sp => sp.GetRequiredService<WindowSelectorWindowFactory>().CreateWindowSelectorWindow());
    }

}
