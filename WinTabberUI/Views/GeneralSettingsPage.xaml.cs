using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WinTabberUI.Services;
using WinTabberUI.ViewModels;
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
