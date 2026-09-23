using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using WinTabber.Interop;
using WinTabber.ViewModels;
using WinTabberUI.Windowing;

namespace WinTabberUI;

public sealed partial class SuspendedWindowsWindow : WinUIEx.WindowEx
{
    private const double BottomMargin = 24;

    private readonly IWindowInterop _windowInterop;
    public SuspendedWindowsViewModel ViewModel { get; }

    public SuspendedWindowsWindow(SuspendedWindowsViewModel viewModel, IWindowInterop windowInterop)
    {
        ViewModel = viewModel;
        _windowInterop = windowInterop;

        InitializeComponent();
        AppWindow.SetIcon(DesktopHelper.AppIconPath);

        // The empty-state TextBlock's Visibility uses a classic Binding (see SuspendedWindowsWindow.xaml's
        // comment for why: an x:Bind with a StaticResource converter can't compile at this file's root
        // binding scope, since the root is a Window, not a FrameworkElement). Classic Binding needs an
        // explicit DataContext to resolve "Items.Count" against — RootGrid is the nearest FrameworkElement
        // ancestor, so it gets the ViewModel directly.
        RootGrid.DataContext = ViewModel;

        // Per the brief and the design spec's backdrop table: WindowEx + DesktopAcrylicBackdrop.
        // Set in code-behind, not XAML, matching DockWindow's and SettingsWindow's established
        // workaround — a `SystemBackdrop="{winuiex:...}"`-style XAML attribute crashes this SDK's
        // XamlCompiler pass2 with no diagnostic (see SettingsWindow.xaml.cs's comment for the
        // confirmed repro). DockWindow's own port (commit 3e3599b) initially missed this same call
        // and had to be fixed after review — set it explicitly here from the start.
        SystemBackdrop = new DesktopAcrylicBackdrop();

        var hwnd = WindowNative.GetWindowHandle(this);

        // Never let this window take focus/activation, even from a mouse click on one of its
        // buttons — that keeps focus on WindowSelectorWindow regardless of show ordering between
        // the two coordinators. Same call the WPF original made via IWindowInterop, just with a
        // WinUI3-obtained handle instead of WindowInteropHelper's.
        _windowInterop.MakeWindowNonActivating(hwnd);

        // VERIFIED via a temporary diagnostic run (Task 4a.5): without this, the window kept
        // whatever default client size WindowEx picks when no Width/Height is set in XAML — a huge
        // fraction of the monitor (observed 2880x1678 physical px on a 3840x2304 desktop) — which
        // also made the centering math below meaningless (a bar that fills most of the screen has
        // no meaningful "center"). WinUIEx has no SizeToContent equivalent in this package version
        // (same gap DockWindow's port documented for its own, cosmetic, height-only case) — this
        // window's whole purpose is a small bar that hugs its content and tracks the cursor, so the
        // gap can't just be left undone here the way DockWindow's could. Re-measures and resizes
        // the real AppWindow client area to the root Grid's natural desired size on every list
        // change, then RootGrid.SizeChanged (fired once the resize's layout pass completes)
        // re-runs PositionWindow with the now-correct Bounds.
        // Deferred via DispatcherQueue, not called directly from the handler: the ReactiveUI
        // pipeline that populates ViewModel.Items runs .ObserveOn(RxApp.MainThreadScheduler), which
        // on WinUI3 posts to this same DispatcherQueue rather than always running synchronously —
        // so CollectionChanged and the empty-state TextBlock's Visibility binding update can land in
        // different dispatcher turns. VERIFIED (Task 4a.5): measuring synchronously inside this
        // handler intermittently captured a stale DesiredSize (the window failed to shrink back down
        // after the last item was resumed); queuing the measure/resize for the next turn fixed it.
        ((System.Collections.Specialized.INotifyCollectionChanged)ViewModel.Items).CollectionChanged +=
            (_, _) => DispatcherQueue.TryEnqueue(() => ResizeToContent(hwnd));
        RootGrid.SizeChanged += (_, _) => PositionWindow(hwnd);
        Activated += (_, _) => PositionWindow(hwnd);

        ResizeToContent(hwnd);
        PositionWindow(hwnd);
    }

    private void ResizeToContent(nint hwnd)
    {
        RootGrid.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = RootGrid.DesiredSize;
        if (desired.Width <= 0 || desired.Height <= 0)
        {
            return;
        }

        var scale = DesktopHelper.GetScaleForWindow(hwnd);
        AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(
            (int)Math.Ceiling(desired.Width * scale),
            (int)Math.Ceiling(desired.Height * scale)));
    }

    private void PositionWindow(nint hwnd)
    {
        // TODO(verify): WPF's original found the screen under the cursor via
        // System.Windows.Forms.Screen.FromPoint(Control.MousePosition) — that WinForms API still
        // works unchanged in a WinUI3 app (confirmed elsewhere in this plan, WindowSelectorViewModel
        // already does the same). The WinUI3-native alternative is
        // Microsoft.UI.Windowing.DisplayArea.GetFromPoint / GetFromWindowId — either is valid; this
        // draft uses the already-proven WinForms path for consistency with WindowSelectorViewModel's
        // CursorScreen, confirm this doesn't diverge from whatever WindowSelectorWindow's own port
        // (a later phase) settles on for the same concept.
        var workingArea = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition).WorkingArea;
        var bounds = DesktopHelper.ToLogicalBounds(hwnd, workingArea);

        // AppWindow.Move takes physical pixels; `bounds` and `Bounds` (Window.Bounds) are both
        // DIPs, so the computed logical top-left must be scaled back up before the Move call.
        // VERIFIED this was wrong before the fix (Task 4a.5): on this environment's scaled desktop,
        // UI Automation's BoundingRectangle showed the window nowhere near bottom-center until this
        // conversion was added.
        var scale = DesktopHelper.GetScaleForWindow(hwnd);
        var x = bounds.Left + (bounds.Width - Bounds.Width) / 2;
        var y = bounds.Bottom - Bounds.Height - BottomMargin;

        AppWindow.Move(new Windows.Graphics.PointInt32((int)(x * scale), (int)(y * scale)));
    }
}
