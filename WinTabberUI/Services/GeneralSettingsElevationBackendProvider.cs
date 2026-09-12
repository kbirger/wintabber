using WinTabber.Interop;
using WinTabberUI.Models.Settings;

namespace WinTabberUI.Services;

/// <summary>
/// Adapts <see cref="GeneralSettings.ElevationBackend" /> to <see cref="IElevationBackendProvider" />
/// so <c>WinTabber.Interop</c>'s <see cref="ElevationLauncherResolver" /> never needs a direct
/// reference to settings.
/// </summary>
public class GeneralSettingsElevationBackendProvider : IElevationBackendProvider
{
    private readonly GeneralSettings _settings;

    public GeneralSettingsElevationBackendProvider(ApplicationSettings settings)
    {
        _settings = settings.General;
    }

    public ElevationBackend Backend => _settings.ElevationBackend;
}
