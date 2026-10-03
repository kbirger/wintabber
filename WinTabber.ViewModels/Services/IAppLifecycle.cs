namespace WinTabberUI.Services;

/// <summary>
/// Abstracts application-level lifecycle actions away from any specific UI framework, so a
/// ViewModel that needs to shut the app down does not have to reference System.Windows.Application
/// directly. See WinTabberUI/ViewModels/NotifyIconViewModel.cs.
/// </summary>
public interface IAppLifecycle
{
    void Shutdown();
}
