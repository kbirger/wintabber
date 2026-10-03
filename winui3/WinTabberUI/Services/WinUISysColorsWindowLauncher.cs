namespace WinTabberUI.Services;

/// <summary>
/// No-op: the SysColors dialog itself is a deliberately deferred port (Task 0.4). This satisfies
/// NotifyIconViewModel's constructor dependency; SysColorsCommand is never exposed in the tray
/// menu, matching the WPF app's own menu, which defines the command but never shows it either.
/// </summary>
public sealed class WinUISysColorsWindowLauncher : ISysColorsWindowLauncher
{
    public void Show() { }
}
