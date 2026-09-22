using Microsoft.Extensions.DependencyInjection;
using System.Reactive.Disposables;
using WinTabber.Api.Windowing;
using WinTabber.Api.Windowing.Suspension;
using WinTabber.Api.Windowing.Thumbnails;
using WinTabber.Events;
using WinTabber.ViewModels;
using WinTabberUI.Coordinators;
using WinTabberUI.Infrastructure;

namespace WinTabberUI;

/// <summary>
/// Ported from the WPF app's own <c>BackgroundServiceContainer</c>. Preloads the same shared state
/// (<see cref="AppCache"/>, <see cref="WindowManager"/>, <see cref="ApplicationStateViewModel"/>)
/// and roots every already-ported coordinator behind one <see cref="CompositeDisposable"/>, replacing
/// the five separate fields <c>App.xaml.cs</c> used to hold individually. Disposal order matches the
/// WPF original: coordinators first, then <see cref="WinTabberEventManager"/>, then
/// <see cref="IProcessSuspensionService"/> (resumes every frozen process), then
/// <see cref="IWindowThumbnailService"/> (restores every off-screen thumbnailed window) last -- so
/// exiting the app never strands suspended or thumbnailed windows with no UI left to bring them back.
/// <see cref="StartupCoordinator"/> and <see cref="MediaDebugWindowCoordinator"/> are not ported to
/// this app yet -- deliberately left out of this composite, not forgotten; add them here when they
/// land.
/// </summary>
public class BackgroundServiceContainer : IDisposable
{
    private readonly CompositeDisposable _cleanup;

    public BackgroundServiceContainer(IServiceProvider ioc)
    {
        ioc.GetRequiredService<WindowManager>();
        ioc.GetRequiredService<ApplicationStateViewModel>();
        ioc.GetRequiredService<AppCache>().Load();

        _cleanup = new CompositeDisposable(
            ioc.GetRequiredService<ThumbnailWindowCoordinator>().Init(),
            ioc.GetRequiredService<MediaControlsWindowCoordinator>(),
            ioc.GetRequiredService<NotifyIconCoordinator>(),
            ioc.GetRequiredService<WindowSelectorWindowCoordinator>(),
            ioc.GetRequiredService<SettingsWindowCoordinator>(),
            ioc.GetRequiredService<SuspendedWindowsWindowCoordinator>(),
            ioc.GetRequiredService<WindowCommandCoordinator>(),
            ioc.GetRequiredService<WinTabberEventManager>(),
            // Disposing this resumes every frozen process on exit. Order within the composite is
            // insertion order and does not matter here: ResumeAll only touches IProcessControl,
            // IWindowVisibility, and the state file, none of which the composite owns.
            ioc.GetRequiredService<IProcessSuspensionService>(),
            // Same idea: disposing this moves every off-screen thumbnailed window back to its
            // original position on exit, so a killed/crashed app doesn't leave windows stranded.
            ioc.GetRequiredService<IWindowThumbnailService>()
        );
    }

    public void Dispose()
    {
        _cleanup.Dispose();
    }
}
