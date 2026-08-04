using ReactiveUI;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows;
using WinTabberUI.ViewModels.Settings;

namespace WinTabberUI.Views
{
    /// <summary>
    /// Interaction logic for GeneralSettingsPage.xaml
    /// </summary>
    public partial class GeneralSettingsPage : ReactivePage<GeneralSettingsViewModel>, IViewFor<GeneralSettingsViewModel>
    {
        public GeneralSettingsPage()
        {
            InitializeComponent();
            DataContextChanged += GeneralSettingsPage_DataContextChanged;
            this.WhenActivated((dispose) =>
            {
                this.Bind(
                    ViewModel,
                    vm => vm.StartupMode,
                    view => view.StartupList.SelectedValue,

                    signalViewUpdate: Observable.FromEventPattern(StartupList, nameof(StartupList.SelectionChanged))
                )
                .DisposeWith(dispose);

                this.Bind(
                    ViewModel,
                    vm => vm.ThumbnailResizeMode,
                    view => view.ThumbnailResizeModeList.SelectedValue,

                    signalViewUpdate: Observable.FromEventPattern(ThumbnailResizeModeList, nameof(ThumbnailResizeModeList.SelectionChanged))
                )
                .DisposeWith(dispose);
            });
            StartupList.LostFocus += StartupList_LostFocus;
        }

        private void StartupList_LostFocus(object sender, RoutedEventArgs e)
        {
            //BindingExpression binding = (BindingExpression)StartupList.GetBindingExpression(ComboBox.SelectedValueProperty);
            //binding.UpdateSource();
        }

        private void GeneralSettingsPage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            ViewModel = e.NewValue as GeneralSettingsViewModel;
        }
    }
}
