// winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinRT;
using WinRT.Interop;
using WinTabber.UI.Media.Services;
using WinTabber.UI.Media.ViewModels;
using WinTabberUI.Windowing;
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
    private readonly nint _hwnd;
    private bool _hasCenteredOnce;

    public MediaControlsViewModel ViewModel { get; }
    private DesktopAcrylicController? _desktopAcrylicController;
    private readonly SystemBackdropConfiguration _configurationSource;

    public MediaControlsWindow(MediaControlsViewModel viewModel, IMediaControlsStateService mediaControlsStateService)
    {
        ViewModel = viewModel;
        _mediaControlsStateService = mediaControlsStateService;
        InitializeComponent();
        //SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
        if (DesktopAcrylicController.IsSupported())
        {
            _desktopAcrylicController = new DesktopAcrylicController();
            _configurationSource = new SystemBackdropConfiguration();
            _desktopAcrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
            _desktopAcrylicController.SetSystemBackdropConfiguration(_configurationSource);
        }
        SetConfigurationSourceTheme();
        RootGrid.DataContext = ViewModel;

        Activated += OnActivated;

        _hwnd = WindowNative.GetWindowHandle(this);

        // WPF original: SizeToContent="Height" with a fixed Width="700". WindowEx/WinUIEx expose no
        // SizeToContent equivalent (same gap SuspendedWindowsWindow.xaml.cs documents) -- reproduced
        // here the same way: measure RootGrid at the window's current client width with unbounded
        // height, then resize just the AppWindow's height to match. Width is never touched, unlike
        // SuspendedWindowsWindow's resize-both-dimensions case, since this window keeps a fixed width.
        // Re-measured on every RootGrid.SizeChanged, not just once at startup: switching the active
        // session changes which of the title/artist/album TextBlocks are visible (each collapses via
        // NullToVisibilityConverter), which changes the content's natural height.
        RootGrid.SizeChanged += (_, _) => DispatcherQueue.TryEnqueue(ResizeHeightToContent);
        ((FrameworkElement)Content).ActualThemeChanged += Window_ThemeChanged;
        ResizeHeightToContent();
    }

    private void Window_ThemeChanged(FrameworkElement sender, object args)
    {
        if (_configurationSource != null)
            SetConfigurationSourceTheme();
    }

    private void SetConfigurationSourceTheme()
    {
        if (_configurationSource != null)
            _configurationSource.Theme =
                (SystemBackdropTheme)((FrameworkElement)Content).ActualTheme;
    }

    private void ResizeHeightToContent()
    {
        var scale = DesktopHelper.GetScaleForWindow(_hwnd);
        var currentSize = AppWindow.ClientSize;
        RootGrid.Measure(new Windows.Foundation.Size(currentSize.Width / scale, double.PositiveInfinity));
        var desiredHeight = RootGrid.DesiredSize.Height;
        if (desiredHeight <= 0)
        {
            return;
        }

        // Floored at MinHeight (DIPs, set in XAML): WinUIEx clamps ResizeClient to MinHeight itself,
        // so an un-floored newHeight below that would never actually apply -- currentSize.Height
        // would keep reporting the clamped value while desiredHeight kept reporting the smaller
        // unclamped one, and the 1px guard below would never converge (a genuine SizeChanged
        // feedback loop, not just resize-triggered rounding noise).
        var newHeight = Math.Max((int)Math.Ceiling(desiredHeight * scale), (int)Math.Ceiling(MinHeight * scale));
        if (Math.Abs(newHeight - currentSize.Height) > 1)
        {
            AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(currentSize.Width, newHeight));
        }

        // WPF original: WindowStartupLocation="CenterScreen", which WPF resolves once, after
        // SizeToContent settles the window's real size, and never again -- the window's position
        // then stays fixed even as later session switches change its height (that is WPF's actual
        // behavior, not an omission here). Centered once for the same reason: this method's first
        // call is the first point RootGrid has a real DesiredSize to center against, not the
        // constructor's XAML placeholder size (Width=700, MinHeight=221).
        if (!_hasCenteredOnce)
        {
            _hasCenteredOnce = true;
            this.CenterOnScreen();
        }
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
