using System.Windows;
using System.Windows.Interop;
using WinTabber.Interop;
using WinTabberUI.Services;
using WinTabber.ViewModels;

namespace WinTabberUI.Views;

/// <summary>
/// Interaction logic for MediaDebugWindow.xaml
/// </summary>
public partial class MediaDebugWindow : Window
{
    private readonly MediaDebugStateService _debugState;
    private readonly IWindowInterop _windowInterop;

    public MediaDebugWindow(
        MediaDebugViewModel viewModel,
        MediaDebugStateService debugState,
        IWindowInterop windowInterop)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _debugState = debugState;
        _windowInterop = windowInterop;
        DataContext = ViewModel;
    }

    public MediaDebugViewModel ViewModel { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // A debug tool must not change the state it reports. Activation of this window makes
        // MediaControlsWindow.OnDeactivated run, which deactivates the media controls view model.
        // The mouse wheel still scrolls a non-activating window.
        nint handle = new WindowInteropHelper(this).Handle;
        _windowInterop.MakeWindowNonActivating(handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        // The coordinator drops its reference when the window closes. Turn the tray toggle off as
        // well, so that the menu state and the window state stay the same.
        ViewModel.Detach();

        if (_debugState.IsEnabled)
        {
            _debugState.Toggle();
        }

        base.OnClosed(e);
    }
}
