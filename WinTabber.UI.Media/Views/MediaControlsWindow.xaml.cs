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

    private void MediaControlsWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if(IsVisible && IsLoaded)
        {
            //PlayPauseButton.Focus();
        }
    }

    private void MediaControlsWindow_Loaded(object sender, RoutedEventArgs e) { }

    protected override void OnActivated(EventArgs e)
    {
        Debug.WriteLine("media controls: window activated");
        base.OnActivated(e);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        // The window hides when the user clicks or types outside of it. This covers the plain
        // case. A dropdown takes the activation away from this window, and after that no further
        // deactivation arrives, so MediaControlsStateService watches the foreground window too.
        Debug.WriteLine("media controls: window deactivated - hiding");
        _mediaControlsStateService.HideView();
        ViewModel?.Activator.Deactivate();
        base.OnDeactivated(e);

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
            ViewModel.ActiveSession.Playback.IsSeeking = true;
        }
    }

    private void Slider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if(sender is Slider slider && ViewModel?.ActiveSession is not null)
        {
            ViewModel.ActiveSession.Playback.IsSeeking = false;

            ViewModel.ActiveSession.Playback.Seek.Execute(TimeSpan.FromSeconds(slider.Value));

        }
    }

}
