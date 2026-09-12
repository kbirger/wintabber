namespace WinTabber.Interop;

/// <summary>
/// Supplies the user's configured elevation backend choice to <see cref="ElevationLauncherResolver" />
/// without that resolver needing a direct reference to wherever settings actually live.
/// </summary>
public interface IElevationBackendProvider
{
    ElevationBackend Backend { get; }
}
