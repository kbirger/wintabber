using CommunityToolkit.Mvvm.DependencyInjection;
using iNKORE.UI.WPF.DragDrop.Utilities;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using WinTabber.Interop;
using WinTabberUI.ViewModels;

namespace WinTabberUI;

/// <summary>
/// Interaction logic for SuspendedWindowsWindow.xaml
/// </summary>
public partial class SuspendedWindowsWindow : Window
{
    private const double BottomMargin = 24;

    public SuspendedWindowsWindow()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<SuspendedWindowsViewModel>();

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
        Ioc.Default.GetRequiredService<IInteropProxy>().MakeWindowNonActivating(handle);

        PositionWindow();
    }

    private void PositionWindow()
    {
        var workingArea = Screen.FromPoint(Control.MousePosition).WorkingArea;
        var screenRect = new Rect(workingArea.Left, workingArea.Top, workingArea.Width, workingArea.Height);

        var dpiScale = VisualTreeHelper.GetDpi(this);
        var bounds = DpiHelper.DeviceRectToLogical(screenRect, dpiScale.DpiScaleX, dpiScale.DpiScaleY);

        Left = bounds.Left + (bounds.Width - ActualWidth) / 2;
        Top = bounds.Bottom - ActualHeight - BottomMargin;
    }
}
