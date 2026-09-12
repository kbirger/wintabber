namespace WinTabber.Interop;

/// <summary>
/// Picks which <see cref="IElevationLauncher" /> backend actually runs an elevated action, per the
/// user's configured <see cref="ElevationBackend" /> — falling back to the built-in launcher if
/// <see cref="ElevationBackend.Gsudo" /> is configured but not actually available (e.g. gsudo
/// isn't installed).
/// </summary>
public class ElevationLauncherResolver : IElevationLauncher
{
    private readonly IElevationLauncher _builtIn;
    private readonly IElevationLauncher _gsudo;
    private readonly IElevationBackendProvider _backendProvider;

    public ElevationLauncherResolver(IElevationLauncher builtIn, IElevationLauncher gsudo, IElevationBackendProvider backendProvider)
    {
        _builtIn = builtIn;
        _gsudo = gsudo;
        _backendProvider = backendProvider;
    }

    public bool IsAvailable => true;

    public void RunElevated(ElevatedWindowAction action, IEnumerable<int> handles)
    {
        var active = _backendProvider.Backend == ElevationBackend.Gsudo && _gsudo.IsAvailable ? _gsudo : _builtIn;
        active.RunElevated(action, handles);
    }
}
