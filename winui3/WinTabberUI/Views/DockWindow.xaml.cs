using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using WinTabber.Api.Windowing;
using WinTabber.Interop;
using WinTabber.ViewModels;
using WinTabberUI.Controls;
using WinTabberUI.Windowing;

namespace WinTabberUI;

public sealed partial class DockWindow : WinUIEx.WindowEx
{
    private readonly WindowManager _windowManager;
    public DockWindowViewModel ViewModel { get; }

    private Windows.Foundation.Rect? _reservedArea;
    private nint _hwnd;

    public DockWindow(WindowManager windowManager, DockWindowViewModel viewModel)
    {
        _windowManager = windowManager;
        ViewModel = viewModel;

        InitializeComponent();

        // Per the brief and the design spec's backdrop table: WindowEx + DesktopAcrylicBackdrop.
        // Set in code-behind, not XAML, matching SettingsWindow's established workaround — a
        // `SystemBackdrop="{winuiex:...}"`-style XAML attribute crashes this SDK's XamlCompiler
        // pass2 with no diagnostic (see SettingsWindow.xaml.cs's comment for the confirmed repro).
        SystemBackdrop = new DesktopAcrylicBackdrop();

        _hwnd = WindowNative.GetWindowHandle(this);

        // Wire WindowThumbnail.TargetWindow for every container the ListView generates — there is
        // no XAML-level way to bind a control property to "the window that hosts me" in WinUI 3.
        //
        // VERIFIED (not the brief's original FindName approach): `root.FindName("PART_Thumbnail")`
        // reliably returns null here — confirmed via a temporary diagnostic log showing
        // InitialiseThumbnail always sees TargetWindow == null on every real run of this window.
        // Unlike WPF, a DataTemplate's realized content in WinUI 3 is not a name scope FindName can
        // walk into from an ItemContainer's ContentTemplateRoot. Falls back to the brief's
        // documented alternative: a VisualTreeHelper walk for the first WindowThumbnail descendant.
        WindowsList.ContainerContentChanging += (_, args) =>
        {
            if (args.ItemContainer.ContentTemplateRoot is FrameworkElement root
                && FindWindowThumbnail(root) is { } thumbnail)
            {
                thumbnail.TargetWindow = this;
            }
        };

        Activated += (_, _) => MakeSpace();
        Closed += OnClosed;
    }

    // FindName does not resolve a DataTemplate's realized content as a name scope in WinUI 3 the
    // way it does in WPF (see the ContainerContentChanging comment above) — walk the visual tree
    // for the first WindowThumbnail descendant instead.
    private static WindowThumbnail? FindWindowThumbnail(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is WindowThumbnail thumbnail)
            {
                return thumbnail;
            }

            if (FindWindowThumbnail(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void MakeSpace()
    {
        if (_reservedArea is not null)
        {
            return;
        }

        var screenArea = DesktopHelper.GetDesktopArea();
        var scale = AppWindow.Size.Width / (double)Bounds.Width;

        _reservedArea = new Windows.Foundation.Rect(
            screenArea.X + Width * scale,
            screenArea.Y,
            screenArea.Width - Width * scale,
            screenArea.Height);
        DesktopHelper.SetDesktopArea(_reservedArea.Value);

        foreach (var window in _windowManager.GetWindows()
            .Where(w => w.State != WindowPlacement.WindowState.Minimized
                && w.State != WindowPlacement.WindowState.Hidden
                && w.Bounds.X < _reservedArea.Value.X
                && w.Bounds.Width > 0))
        {
            if (!window.Process.IsProcessElevated)
            {
                window.MoveTo(new System.Drawing.Point((int)_reservedArea.Value.X, window.Bounds.Y));
            }
        }
    }

    // WinUI 3's Window has no overridable OnClosed (unlike WPF's Window.OnClosing) — Closed is a
    // plain event, wired up in the constructor above.
    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_reservedArea is not null)
        {
            var screenArea = DesktopHelper.GetDesktopArea();
            DesktopHelper.SetDesktopArea(new Windows.Foundation.Rect(
                screenArea.X, screenArea.Y, screenArea.Width, _reservedArea.Value.Height));
            _reservedArea = null;
        }
    }
}
