namespace WinTabber.Interop;

/// <summary>
/// Runs an action against a batch of window handles from an elevated context, bridging the UIPI
/// boundary that blocks WM_CLOSE/ShowWindow/etc. from this (non-elevated) process to a
/// higher-integrity window. Implementations decide *how* to get elevated; callers only care that
/// all of <paramref name="handles"/> get <paramref name="action"/> applied, batched into as few
/// elevation prompts as the implementation can manage.
/// </summary>
public interface IElevationLauncher
{
    /// <summary>Whether this launcher's backend can currently be used (e.g. an external tool is installed).</summary>
    bool IsAvailable { get; }

    void RunElevated(ElevatedWindowAction action, IEnumerable<int> handles);
}
