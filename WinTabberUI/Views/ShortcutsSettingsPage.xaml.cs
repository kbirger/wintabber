using ReactiveUI;
using System.Windows;
using WinTabberUI.ViewModels.Settings;

namespace WinTabberUI.Views
{
    /// <summary>
    /// Interaction logic for ShortcutsSettingsPage.xaml
    /// </summary>
    public partial class ShortcutsSettingsPage
        : ReactivePage<ShortcutsSettingsViewModel>,
            IViewFor<ShortcutsSettingsViewModel>
    {
        public ShortcutsSettingsPage()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            ViewModel = e.NewValue as ShortcutsSettingsViewModel;
        }
    }
}
