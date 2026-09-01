using Microsoft.Extensions.DependencyInjection;
using System.Reactive.Disposables;
using WinTabber.Api.Media.ShellApplications.Repositories;
using WinTabber.API;
using WinTabber.API.Suspension;
using WinTabber.API.Thumbnails;
using WinTabber.Events;
using WinTabber.Interop;
using WinTabberUI.Coordinators;
using WinTabberUI.Infrastructure;
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
        // Must happen before any suspend attempt; OpenProcess on another user's process needs it.
        ioc.GetRequiredService<IInteropProxy>().EnableDebugPrivilege();
        ioc.GetRequiredService<InstalledApplicationRepository>();

        _cleanup = new CompositeDisposable(
            ioc.GetRequiredService<StartupCoordinator>(),
            ioc.GetRequiredService<SettingsWindowViewCoordinator>().Init(),
            ioc.GetRequiredService<WindowSelectorViewCoordinator>().Init(),
            ioc.GetRequiredService<MediaWindowViewCoordinator>().Init(),
            ioc.GetRequiredService<SuspendedWindowsViewCoordinator>().Init(),
            // Must come after MediaWindowViewCoordinator: both react to the same visibility
            // subject, and the debug window must not open before the window it observes.
            ioc.GetRequiredService<MediaDebugWindowCoordinator>().Init(),
            ioc.GetRequiredService<ThumbnailWindowCoordinator>().Init(),
            ioc.GetRequiredService<WindowCommandCoordinator>(),
            ioc.GetRequiredService<WinTabberEventManager>(),
            ioc.GetRequiredService<NotifyIconCoordinator>(),
            // Disposing this resumes every frozen process on exit. Order within the composite is
            // insertion order and does not matter here: ResumeAll only touches IInteropProxy and
            // the state file, neither of which the composite owns.
            ioc.GetRequiredService<IProcessSuspensionService>(),
            // Same idea: disposing this moves every off-screen thumbnailed window back to its
            // original position on exit, so a killed/crashed app doesn't leave windows stranded.
            ioc.GetRequiredService<IWindowThumbnailService>()
            //ioc.GetRequiredService<IAudioDeviceManager>().Init()
        );





    }
    public void Dispose()
    {
        _cleanup?.Dispose();
    }
}
