using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Reactive.Concurrency;
using System.Windows;
using WinTabber.Api.Media.CoreAudio;
using WinTabber.Api.Media.CoreAudio.Repositories;
using WinTabber.Api.Media.CoreAudio.Services;
using WinTabber.Api.Media.Repositories;
using WinTabber.Api.Media.ShellApplications.Repositories;
using WinTabber.Api.Media.SMTC.Repositories;
using WinTabber.API;
using WinTabber.API.Suspension;
using WinTabber.Events;
using WinTabber.Interop;
using WinTabber.UI.Media.Services;
using WinTabber.UI.Media.ViewModels;
using WinTabber.UI.Media.ViewModels.Factories;
using WinTabberUI.Coordinators;
using WinTabberUI.Infrastructure;
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
            .AddFactories()
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

    private static IServiceCollection AddFactories(this IServiceCollection services)
    {
        return services
            .AddSingleton<AudioDeviceSelectorViewModelFactory>()
            .AddSingleton<MediaSessionViewModelFactory>();
    }

    private static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        return services
            .AddSingleton<AutoStartupService>()
            .AddSingleton<BackgroundServiceContainer>();
    }
    private static IServiceCollection AddDomainModels(this IServiceCollection services)
    {
        return services
            .AddKeyedSingleton<IScheduler>(STAScheduler.Key, (_, _) => STAScheduler.Create())
            .AddSingleton<InputListenerService>()
            .AddSingleton<WinTabberEventManager>()
            .AddSingleton<ApplicationState>()
            .AddSingleton<IInteropProxy, InteropProxy>()
            .AddSingleton<IProcessRepository, ProcessRepository>()
            .AddSingleton<WindowManager>()
            //.AddSingleton<IAudioDeviceManager, AudioDeviceManager>()
            .AddSingleton<ISuspensionStrategy, NtProcessSuspensionStrategy>()
            .AddSingleton<ISuspensionStrategy, ThreadSuspensionStrategy>()
            .AddSingleton<ISuspendedWindowStore>(_ => new SuspendedWindowFileStore(Paths.SuspensionDirectory))
            .AddSingleton<IProcessSuspensionService, ProcessSuspensionService>()
            .AddSingleton<AppCache>()
            .AddSingleton<IMMDeviceEnumeratorWrapper, MMDeviceEnumeratorWrapper>()
            .AddSingleton<CoreAudioDeviceRepository>(sp =>
                new CoreAudioDeviceRepository(
                    sp.GetRequiredKeyedService<IScheduler>(STAScheduler.Key),
                    sp.GetRequiredService<IMMDeviceEnumeratorWrapper>()))
            .AddSingleton<CoreAudioSessionRepository>(sp =>
                new CoreAudioSessionRepository(sp.GetRequiredKeyedService<IScheduler>(STAScheduler.Key)))
            .AddSingleton<SMTCSessionRepository>()
            .AddSingleton<MediaSessionService>()
            .AddSingleton<AudioSessionService>()
            .AddSingleton<AudioDeviceService>()
            .AddSingleton<InstalledApplicationRepository>();
    }
    private static IServiceCollection AddUpdaters(this IServiceCollection services)
    {
        return services
            .AddSingleton<WindowHistoryUpdater>();
    }

    private static IServiceCollection AddCoordinators(this IServiceCollection services)
    {
        return services
            .AddSingleton<StartupCoordinator>()
            .AddSingleton<WindowSelectorViewCoordinator>()
            .AddSingleton<SettingsWindowViewCoordinator>()
            .AddSingleton<MediaWindowViewCoordinator>()
            .AddSingleton<WindowCommandCoordinator>()
            .AddSingleton<NotifyIconCoordinator>()
            .AddSingleton<SuspendedWindowsViewCoordinator>();

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
            .AddSingleton<NotifyIconViewModel>()
            .AddSingleton<SuspendedWindowsViewModel>();
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
            .AddTransient<SuspendedWindowsWindow>()
            .AddSingleton<WindowSelectorWindowFactory>()
            .AddSingleton(sp => sp.GetRequiredService<WindowSelectorWindowFactory>().CreateWindowSelectorWindow());
    }

}
