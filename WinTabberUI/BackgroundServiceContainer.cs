using Microsoft.Extensions.DependencyInjection;
using System.Reactive.Disposables;
using Windows.Media.Devices;
using WinTabber.API;
using WinTabber.Events;
using WinTabberUI.Coordinators;
using WinTabberUI.Infrastructure;
using WinTabberUI.Services;
using WinTabberUI.Updaters;
using WinTabberUI.ViewModels;

namespace WinTabberUI;

public class BackgroundServiceContainer : IDisposable
{
    private CompositeDisposable _cleanup;

    public BackgroundServiceContainer(IServiceProvider ioc)
    {
        ioc.GetRequiredService<WindowSelectorWindow>();
        ioc.GetRequiredService<ApplicationStateViewModel>();
        ioc.GetRequiredService<SettingsViewModel>();
        ioc.GetRequiredService<WindowManager>();
        ioc.GetRequiredService<AppCache>().Load();
        ioc.GetRequiredService<InstalledApplicationService>();

        _cleanup = new CompositeDisposable(
            ioc.GetRequiredService<StartupCoordinator>(),
            ioc.GetRequiredService<SettingsWindowViewCoordinator>().Init(),
            ioc.GetRequiredService<WindowSelectorViewCoordinator>().Init(),
            ioc.GetRequiredService<MediaWindowViewCoordinator>().Init(),
            ioc.GetRequiredService<WindowHistoryUpdater>().Init(),
            ioc.GetRequiredService<WindowCommandCoordinator>(),
            ioc.GetRequiredService<WinTabberEventManager>(),
            ioc.GetRequiredService<NotifyIconCoordinator>()
            //ioc.GetRequiredService<IAudioDeviceManager>().Init()
        );





    }
    public void Dispose()
    {
        _cleanup?.Dispose();
    }
}
