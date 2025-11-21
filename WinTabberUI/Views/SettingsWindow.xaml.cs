using CommunityToolkit.Mvvm.DependencyInjection;
using ControlzEx;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables.Fluent;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinTabberUI.Models.Settings;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Views
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow //ReactiveWindow<SettingsViewModel>
    {

        public SettingsWindow()
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

            DataContext = Ioc.Default.GetRequiredService<SettingsViewModel>();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DataContext = e.NewValue as SettingsViewModel;
        }
    }
}
