using Microsoft.UI.Xaml;
using ReactiveUI;
using WinTabber.ViewModels.Settings;

namespace WinTabberUI.Views;

// See GeneralSettingsPageBase.cs for the full rationale (microsoft/microsoft-ui-xaml#7746 XAML
// compiler bug with generic ReactivePage<T> roots, plus ReactivePage<T> not auto-wiring ViewModel
// from DataContext in either ReactiveUI.Wpf or ReactiveUI.WinUI). Same pattern, applied here.
public class AppearanceSettingsPageBase : ReactivePage<AppearanceSettingsViewModel>
{
    public AppearanceSettingsPageBase()
    {
        DataContextChanged += (sender, e) => ViewModel = e.NewValue as AppearanceSettingsViewModel;
    }
}
