using CommunityToolkit.Mvvm.DependencyInjection;
using ReactiveUI;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinTabber.UI.Media.Services;
using WinTabber.UI.Media.ViewModels;

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
