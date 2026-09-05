namespace WinTabber.Interop;

/// <summary>
/// Generic window show/hide operations, split out of <see cref="IWindowInterop"/> so a consumer
/// that only needs visibility (e.g. process suspension, which hides a window while its process is
/// frozen) doesn't have to depend on the full window-interop surface.
/// </summary>
public interface IWindowVisibility
{
    /// <summary>Hides a window (ShowWindow SW_HIDE). No-op if the handle is not a window.</summary>
    void HideWindow(int handle);

    /// <summary>Restores and foregrounds a window (ShowWindow SW_RESTORE + SetForegroundWindow). No-op if the handle is not a window.</summary>
    void RestoreWindow(int handle);
}
