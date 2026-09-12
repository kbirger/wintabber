namespace WinTabberUI.Services;

/// <summary>
/// Abstracts showing the system-colors debug window away from any specific UI framework, so a
/// ViewModel that needs to open it does not have to reference the WPF window type directly. See
/// WinTabber.ViewModels/NotifyIconViewModel.cs.
/// </summary>
public interface ISysColorsWindowLauncher
{
    void Show();
}
