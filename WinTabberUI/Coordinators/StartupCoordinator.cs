using ReactiveUI;
using WinTabberUI.Services;
using WinTabber.ViewModels;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Ported unchanged from the WPF app's own <see cref="StartupCoordinator"/>: applies the settings
/// page's chosen <see cref="Services.StartupMode"/> (registry run key vs. logon scheduled task) via
/// <see cref="AutoStartupService"/> whenever it changes. No thread marshal needed -- unlike a
/// coordinator that touches UI, this only does registry/Task Scheduler I/O.
/// </summary>
public class StartupCoordinator : IDisposable
{
    private readonly IDisposable _settingsChanges;

    public StartupCoordinator(SettingsViewModel vm, AutoStartupService autoStartupService)
    {
        _settingsChanges = vm.General.WhenAnyValue(x => x.StartupMode)
            .Subscribe(mode => autoStartupService.EnsureStartupMode(mode));
    }

    public void Dispose()
    {
        _settingsChanges.Dispose();
    }
}
