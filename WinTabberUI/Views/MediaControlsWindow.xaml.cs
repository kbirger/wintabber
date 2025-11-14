using CommunityToolkit.Mvvm.DependencyInjection;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
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
using WinTabberUI.ViewModels;

namespace WinTabberUI;

/// <summary>
/// Interaction logic for MediaControlsWindow.xaml
/// </summary>
public partial class MediaControlsWindow
{
    public MediaControlsWindow()
    {
        DataContext = Ioc.Default.GetRequiredService<MediaControlsViewModel>();
        InitializeComponent();
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
        Close();
        base.OnLostFocus(e);
    }
}
