using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using WinTabber.Interop;
using WinTabberUI.ViewModels;
using WinTabberUI.Windowing;

namespace WinTabberUI;

/// <summary>
/// Interaction logic for SuspendedWindowsWindow.xaml
/// </summary>
public partial class SuspendedWindowsWindow : Window
{
    private const double BottomMargin = 24;

    private readonly IWindowInterop _windowInterop;

    public SuspendedWindowsWindow(SuspendedWindowsViewModel viewModel, IWindowInterop windowInterop)
    {
        InitializeComponent();
        _windowInterop = windowInterop;
        DataContext = viewModel;

        SizeChanged += (_, _) => PositionWindow();
        IsVisibleChanged += (_, e) =>
        {
            if (bool.Equals(e.NewValue, true))
            {
                PositionWindow();
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Never let this window take focus/activation, even from a mouse click on one of its
        // buttons — that keeps focus on WindowSelectorWindow regardless of show ordering between
        // the two coordinators.
        nint handle = new WindowInteropHelper(this).Handle;
        _windowInterop.MakeWindowNonActivating(handle);

        PositionWindow();
    }

    private void PositionWindow()
    {
        var workingArea = Screen.FromPoint(Control.MousePosition).WorkingArea;
        var bounds = this.ToLogicalBounds(workingArea);

        Left = bounds.Left + (bounds.Width - ActualWidth) / 2;
        Top = bounds.Bottom - ActualHeight - BottomMargin;
    }
}
