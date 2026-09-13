using Microsoft.Extensions.DependencyInjection;
using WinTabber.Api.Windowing;
using WinTabber.Api.Windowing.Suspension;
using WinTabber.Api.Windowing.Thumbnails;
using WinTabber.Events;
using WinTabber.Events.Shortcuts;
using WinTabber.Interop;
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
            // Transient: DockWindow is a WinUI 3 Window, and a Window can only be shown once — a
            // future coordinator (Phase 5) needs to be able to construct a fresh one each time it
            // docks a new application, not reuse a disposed Window instance from the container.
            .AddTransient<DockWindow>();
    }
}
