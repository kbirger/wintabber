using Microsoft.Extensions.DependencyInjection;
using System.Reactive.Disposables;
using WinTabber.API;
using WinTabber.Events;
using WinTabberUI.Coordinators;
using WinTabberUI.Infrastructure;
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
        ioc.GetRequiredService<ImageCache>().Load();
        _cleanup = new CompositeDisposable(
            ioc.GetRequiredService<StartupCoordinator>(),
            ioc.GetRequiredService<SettingsWindowViewCoordinator>().Init(),
            ioc.GetRequiredService<WindowSelectorViewCoordinator>().Init(),
            ioc.GetRequiredService<MediaWindowViewCoordinator>().Init(),
            ioc.GetRequiredService<WindowHistoryUpdater>().Init(),
            ioc.GetRequiredService<WindowCommandCoordinator>(),
            ioc.GetRequiredService<WinTabberEventManager>(),
            ioc.GetRequiredService<NotifyIconCoordinator>()
        );





    }
    public void Dispose()
    {
        _cleanup?.Dispose();
    }
}
