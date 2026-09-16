// winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinTabber.UI.Media.Services;
using WinTabber.UI.Media.ViewModels;
using WinUIEx;

namespace WinTabberUI.Views;

// Follows WindowSelectorWindow.xaml.cs's established precedent, not ReactiveUI's
// IViewFor<T>/WhenActivated pattern: no window ported in this migration uses that pattern (only
// the two Settings Pages do), and WindowEx is not a FrameworkElement, so every converter-carrying
// binding in this window's XAML uses classic {Binding} against RootGrid.DataContext, not x:Bind.
//
// REAL BUG, caught from reading the WPF original before porting, not discovered live: WPF's
// OnActivated/OnDeactivated are two separate overrides (OnDeactivated calls HideView()
// unconditionally). WinUI 3 collapses both directions into one Activated event -- the exact bug
// class that cost WindowSelectorWindow its final live-verification round (see that file's
// Activated handler). Guarded from the start here instead of found live.
public sealed partial class MediaControlsWindow : WindowEx
{
    private readonly IMediaControlsStateService _mediaControlsStateService;

    public MediaControlsViewModel ViewModel { get; }

    public MediaControlsWindow(MediaControlsViewModel viewModel, IMediaControlsStateService mediaControlsStateService)
    {
        ViewModel = viewModel;
        _mediaControlsStateService = mediaControlsStateService;
        InitializeComponent();

        SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
        RootGrid.DataContext = ViewModel;

        Activated += OnActivated;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            // The window hides when the user clicks or types outside of it. This covers the plain
            // case. A dropdown takes the activation away from this window, and after that no
            // further deactivation arrives, so MediaControlsStateService watches the foreground
            // window too -- ported from the WPF original's OnDeactivated doc comment.
            _mediaControlsStateService.HideView();
            ViewModel.Activator.Deactivate();
            return;
        }

        // Counterpart to the branch above: without this, the view model's WhenActivated block
        // would run once ever instead of once per show, and every session/device subscription
        // would go dead after the first hide.
        ViewModel.Activator.Activate();
    }

    /// <summary>
    /// Adaptation of the WPF original's Slider.Thumb.DragStarted/DragCompleted handlers: WinUI 3's
    /// Slider does not expose its internal Thumb's drag events directly. Pointer press/release on
    /// the Slider itself brackets a drag the same way -- IsSeeking suppresses the position
    /// extrapolation pipeline while the user has the thumb, and the seek commits on release.
    /// </summary>
    private void PositionSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.ActiveSession is not null)
        {
            ViewModel.ActiveSession.Playback.IsSeeking = true;
        }
    }

    private void PositionSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Slider slider && ViewModel.ActiveSession is not null)
        {
            ViewModel.ActiveSession.Playback.IsSeeking = false;
            ViewModel.ActiveSession.Playback.Seek.Execute(TimeSpan.FromSeconds(slider.Value)).Subscribe();
        }
    }
}
