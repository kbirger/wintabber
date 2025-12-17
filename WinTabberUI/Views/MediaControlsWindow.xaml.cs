using CommunityToolkit.Mvvm.DependencyInjection;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinTabber.Interop;
using WinTabberUI.Services;
using WinTabberUI.ViewModels;

namespace WinTabberUI;

/// <summary>
/// Interaction logic for MediaControlsWindow.xaml
/// </summary>
public partial class MediaControlsWindow : IViewFor<MediaControlsViewModel>
{
    public MediaControlsWindow()
    {
        InitializeComponent();
        ViewModel = Ioc.Default.GetRequiredService<MediaControlsViewModel>();
        DataContext = ViewModel;
        this.WhenActivated((CompositeDisposable disposables) => 
        {
            Disposable.Create(() =>
            {
                Debug.WriteLine("deactivate media window");
            }).DisposeWith(disposables);
            //this.Bind(
            //    ViewModel,
            //    vm => vm.ActiveSession,
            //    view => view.SessionSelector.SelectedItem
            //);
        });

        Loaded += MediaControlsWindow_Loaded;
    }

    private void MediaControlsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        //HintService.ShowHints(this);
    }

    protected override void OnActivated(EventArgs e)
    {
        Focus();
        base.OnActivated(e);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        //Debug.WriteLine("selection changed");
    }
}
