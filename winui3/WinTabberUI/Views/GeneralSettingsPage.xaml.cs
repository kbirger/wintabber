using ReactiveUI;
using WinTabber.ViewModels.Settings;

namespace WinTabberUI.Views;

public sealed partial class GeneralSettingsPage : GeneralSettingsPageBase, IViewFor<GeneralSettingsViewModel>
{
    public GeneralSettingsPage()
    {
        InitializeComponent();
    }
}
