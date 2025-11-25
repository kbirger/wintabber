using ReactiveUI;
using WinTabberUI.Services;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Coordinators;

public class StartupCoordinator : IDisposable
{
    private IDisposable _settingsChanges;

    public StartupCoordinator(SettingsViewModel vm, AutoStartupService autoStartupService)
    {
        _settingsChanges = vm.General.WhenAnyValue(x => x.StartupMode)
            .Subscribe((mode) => autoStartupService.EnsureStartupMode(mode));
    }
    public void Dispose()
    {
        _settingsChanges.Dispose();
    }
}
