using System.Windows;
using WinTabber.ViewModels;

namespace WinTabberUI.Views
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow //ReactiveWindow<SettingsViewModel>
    {

        public SettingsWindow(SettingsViewModel viewModel)
        {
            InitializeComponent();
            //this.WhenActivated(dispose =>
            //{
            //    this.OneWayBind(
            //        this.ViewModel,
            //        vm => vm.Sections,
            //        view => view.SettingsCategoryList.ItemsSource)
            //    .DisposeWith(dispose);
                
            //    this.Bind(
            //        this.ViewModel,
            //        vm => vm.SelectedView,
            //        view => view.SettingsCategoryList.SelectedItem
            //    ).DisposeWith(dispose);

            //    this.OneWayBind(
            //        this.ViewModel,
            //        vm => vm.SelectedView,
            //        view => view.SettingsFrame.Content
            //    ).DisposeWith(dispose);
            //});

            DataContext = viewModel;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DataContext = e.NewValue as SettingsViewModel;
        }
    }
}
