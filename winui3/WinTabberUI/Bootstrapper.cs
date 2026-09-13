using Microsoft.Extensions.DependencyInjection;
using WinTabber.Events;
using WinTabber.Events.Shortcuts;
using WinTabber.Interop;
using WinTabberUI.Models.Settings;
using WinTabber.ViewModels;

namespace WinTabberUI;

public static class Bootstrapper
{
    public static ServiceProvider Init()
    {
        return new ServiceCollection()
            .AddCoreServices()
            .AddSettingsGraph()
            .BuildServiceProvider();
    }

    private static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        return services
            .AddSingleton<InteropProxy>()
            .AddSingleton<IProcessControl>(sp => sp.GetRequiredService<InteropProxy>())
            .AddSingleton<IWindowPlacement>(sp => sp.GetRequiredService<InteropProxy>())
            .AddSingleton<IWindowInterop>(sp => sp.GetRequiredService<InteropProxy>())
            .AddSingleton<IWindowVisibility>(sp => sp.GetRequiredService<InteropProxy>())
            .AddSingleton<InputListenerService>();
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
}
