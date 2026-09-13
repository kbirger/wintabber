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

    private readonly nint _hwnd;

    // The reservation MakeSpace computed and applied — used to reposition windows against.
    private Windows.Foundation.Rect? _reservedArea;

    // The work area as it was BEFORE MakeSpace shrank it, captured once, up front. OnClosed
    // restores from this saved value directly rather than re-reading GetDesktopArea() (which,
    // once MakeSpace has run, always returns the already-shrunk area — restoring from that is an
    // identity write that leaves the desktop permanently narrower after every open/close cycle).
    private Windows.Foundation.Rect? _originalDesktopArea;

    public DockWindow(WindowManager windowManager, DockWindowViewModel viewModel)
    {
        _windowManager = windowManager;
        ViewModel = viewModel;

        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);

        // Per the brief and the design spec's backdrop table: WindowEx + DesktopAcrylicBackdrop.
        // Set in code-behind, not XAML, matching SettingsWindow's established workaround — a
        // `SystemBackdrop="{winuiex:...}"`-style XAML attribute crashes this SDK's XamlCompiler
        // pass2 with no diagnostic (see SettingsWindow.xaml.cs's comment for the confirmed repro).
        SystemBackdrop = new DesktopAcrylicBackdrop();

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
        // Bounds.Width can still be 0 at Activated time, before layout has run — guard the same
        // way the WPF original did (`_rect is null && ActualWidth > 0`), otherwise scale becomes
        // Infinity and propagates into a reservation rect with Infinity/-Infinity components,
        // which then gets cast to int when written via SetDesktopArea — garbage written to a
        // global system display setting. Leaving _reservedArea null here means the next
        // Activated firing (once layout has run) retries.
        if (_reservedArea is not null || !(Bounds.Width > 0))
        {
            return;
        }

        var screenArea = DesktopHelper.GetDesktopArea();
        _originalDesktopArea = screenArea;

        // AppWindow.Size is physical pixels for the whole window (frame included); Bounds is DIPs
        // for the client area only — their ratio is inflated by the non-client border, not a
        // clean DPI scale factor. Use the established DIP-to-physical-pixel helper instead (added
        // in Task 4a.5 for this exact conversion problem).
        var scale = DesktopHelper.GetScaleForWindow(_hwnd);

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
        if (_originalDesktopArea is not null)
        {
            // Restore from the pre-shrink value captured in MakeSpace, not a freshly-read
            // GetDesktopArea() — by this point that call would only ever return the already-
            // shrunk area, making the restore an identity write (see field comment above).
            DesktopHelper.SetDesktopArea(_originalDesktopArea.Value);
            _originalDesktopArea = null;
        }

        _reservedArea = null;
    }
}
