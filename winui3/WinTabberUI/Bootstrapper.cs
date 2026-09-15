using Microsoft.Extensions.DependencyInjection;
using WinTabber.Api.Windowing;
using WinTabber.Api.Windowing.Suspension;
using WinTabber.Api.Windowing.Thumbnails;
using WinTabber.Events;
using WinTabber.Events.Shortcuts;
using WinTabber.Interop;
using WinTabber.UI.Media.Services;
using WinTabberUI.Models.Settings;
using WinTabber.ViewModels;
using WinTabberUI.Services;

namespace WinTabberUI;

public static class Bootstrapper
{
    public static ServiceProvider Init()
    {
        return new ServiceCollection()
            .AddCoreServices()
            .AddSettingsGraph()
            .AddDockAndSuspendedWindowsGraph()
            .AddWindowSelectorGraph()
            .AddThumbnailWindowGraph()
            .BuildServiceProvider();
    }

    private static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        return services
            .AddSingleton<BuiltInElevationLauncher>()
            .AddSingleton<IElevationBackendProvider, GeneralSettingsElevationBackendProvider>()
            .AddSingleton<IElevationLauncher>(sp => new ElevationLauncherResolver(
                sp.GetRequiredService<BuiltInElevationLauncher>(),
                sp.GetRequiredService<GsudoElevationLauncher>(),
                sp.GetRequiredService<IElevationBackendProvider>()))
            .AddSingleton<InteropProxy>()
            .AddSingleton<IProcessControl>(sp => sp.GetRequiredService<InteropProxy>())
            .AddSingleton<IWindowPlacement>(sp => sp.GetRequiredService<InteropProxy>())
            .AddSingleton<IWindowInterop>(sp => sp.GetRequiredService<InteropProxy>())
            .AddSingleton<IWindowVisibility>(sp => sp.GetRequiredService<InteropProxy>())
            .AddSingleton<InputListenerService>()
            .AddSingleton<IProcessRepository, ProcessRepository>()
            .AddSingleton<WindowManager>()
            .AddSingleton<ISuspensionStrategy, NtProcessSuspensionStrategy>()
            .AddSingleton<ISuspensionStrategy, ThreadSuspensionStrategy>()
            .AddSingleton<ISuspendedWindowStore>(_ => new SuspendedWindowFileStore(Paths.SuspensionDirectory))
            .AddSingleton<IProcessSuspensionService, ProcessSuspensionService>()
            .AddSingleton<IWindowThumbnailService, WindowThumbnailService>();
    }

    private static IServiceCollection AddSettingsGraph(this IServiceCollection services)
    {
        return services
            // Single shared instance: the settings page mutates this object and calls Save(), so a
            // second Load() elsewhere would silently diverge from what the user sees.
            .AddSingleton<ApplicationSettings>(_ => ApplicationSettings.Load())
            // The live keymap. Seeded from settings.json so the very first hotkey registration
            // already uses the user's bindings; the settings page pushes replacements on save.
            .AddSingleton<IShortcutMapProvider>(sp => new ShortcutMapProvider(
                sp.GetRequiredService<ApplicationSettings>().Shortcuts.ToMap()))
            .AddSingleton<WinTabberEventManager>()
            .AddSingleton<GsudoElevationLauncher>()
            .AddSingleton<SettingsViewModel>();
    }

    private static IServiceCollection AddDockAndSuspendedWindowsGraph(this IServiceCollection services)
    {
        return services
            .AddSingleton<DockWindowViewModel>()
            .AddSingleton<SuspendedWindowsViewModel>()
            // Transient: DockWindow and SuspendedWindowsWindow are WinUI 3 Windows, and a Window can
            // only be shown once — a future coordinator (Phase 5) needs to be able to construct a
            // fresh one each time it docks a new application or shows the suspended-windows bar, not
            // reuse a disposed Window instance from the container. Task 4a.1 registered
            // SuspendedWindowsViewModel but never the window itself — same gap Task 4a.4 found and
            // fixed for DockWindow; fixed here for SuspendedWindowsWindow before it ships.
            .AddTransient<DockWindow>()
            .AddTransient<SuspendedWindowsWindow>();
    }

    private static IServiceCollection AddWindowSelectorGraph(this IServiceCollection services)
    {
        return services
            .AddSingleton<IActiveWindowStateService, ActiveWindowStateService>()
            .AddSingleton<IMediaControlsStateService, StubMediaControlsStateService>()
            .AddSingleton<ApplicationStateViewModelFactory>()
            .AddSingleton(sp => sp.GetRequiredService<ApplicationStateViewModelFactory>().CreateApplicationStateViewModel())
            .AddSingleton<WindowSelectorViewModel>()
            // Transient, same reasoning as DockWindow/SuspendedWindowsWindow (Task 4a.4's fix, reapplied
            // to every window since): a WinUI 3 Window can only be shown once, so the container must
            // hand back a fresh instance on every resolve rather than a disposed singleton.
            .AddTransient<Views.WindowSelectorWindow>();
    }

    // Settles the carried-forward M8 review item (Phase 4a's final review): this port groups DI
    // registrations per window/feature area (AddSettingsGraph, AddDockAndSuspendedWindowsGraph,
    // AddWindowSelectorGraph, this one), not per kind the way the WPF original's Bootstrapper does
    // (AddCoordinators/AddStateServices/AddViewModels/AddViews, each spanning every window). Decision:
    // keep the per-window grouping already established by three precedents rather than switch to
    // per-kind now that a fourth window needs one -- it reads locally coherent (everything one window
    // needs lives in one method) at the cost of the WPF layout's cross-window kind-grouping. Revisit
    // only if a future window's DI graph turns out to overlap heavily with another's.
    private static IServiceCollection AddThumbnailWindowGraph(this IServiceCollection services)
    {
        return services
            .AddTransient<ThumbnailWindowViewModel>()
            // Transient, same reasoning as every other Window registered above: a WinUI 3 Window can
            // only be shown once, and this one is explicitly multi-instance (one per thumbnailed
            // window) besides.
            .AddTransient<Views.ThumbnailWindow>()
            // Singleton, rooted explicitly in App.xaml.cs's OnLaunched (not left to incidental
            // transitive resolution through whatever window happens to be shown first) -- its
            // subscriptions must stay alive for the app's lifetime, the same requirement WPF's
            // BackgroundServiceContainer existed to guarantee.
            .AddSingleton<Coordinators.ThumbnailWindowCoordinator>();
    }
}
