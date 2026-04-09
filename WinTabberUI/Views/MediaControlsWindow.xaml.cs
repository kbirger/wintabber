using CommunityToolkit.Mvvm.DependencyInjection;
using iNKORE.UI.WPF.Helpers;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Runtime.InteropServices;
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
using Windows.UI.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using WinTabber.Interop;
using WinTabberUI.Services;
using WinTabberUI.ViewModels;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace WinTabberUI;

/// <summary>
/// Interaction logic for MediaControlsWindow.xaml
/// </summary>
public partial class MediaControlsWindow : IViewFor<MediaControlsViewModel>, IActivatableView
{
    private IMediaControlsStateService _mediaControlsStateService;
    public MediaControlsWindow()
    {
        InitializeComponent();
        ViewModel = Ioc.Default.GetRequiredService<MediaControlsViewModel>();
        _mediaControlsStateService = Ioc.Default.GetRequiredService<IMediaControlsStateService>();
        Loaded += OnLoaded;
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
        IsVisibleChanged += MediaControlsWindow_IsVisibleChanged;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        //ApplyOpacity(30);

        return base.MeasureOverride(availableSize);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        Debug.WriteLine("X");
        //ApplyOpacity(30);
    }
    

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        //ApplyOpacity(30);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        //ApplyOpacity(40);
    }
    private void MediaControlsWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if(IsVisible && IsLoaded)
        {
            //PlayPauseButton.Focus();
        }
    }

    private void MediaControlsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        //HintService.ShowHints(this);
    }

    protected override void OnActivated(EventArgs e)
    {
        //Focus();
        //PlayPauseButton.Focus();

        base.OnActivated(e);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        //_mediaControlsStateService.HideView();
        //WindowHelper.Reset(this);
        ViewModel?.Activator.Deactivate();
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


    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {

    }

    private void Slider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        if (ViewModel?.ActiveSession is not null)
        {
            ViewModel.ActiveSession.IsSeeking = true;
        }
    }

    private void Slider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if(sender is Slider slider && ViewModel?.ActiveSession is not null)
        {
            ViewModel.ActiveSession.IsSeeking = false;

            ViewModel.ActiveSession.Seek.Execute(TimeSpan.FromSeconds(slider.Value));

        }
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        //ApplyOpacity(100); // fully opaque initially
    }

    void OnSetOpacity(object sender, RoutedEventArgs e)
    {
        //ApplyOpacity(128); // 50%
    }

   
}
