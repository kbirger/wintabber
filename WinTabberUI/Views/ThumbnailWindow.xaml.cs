using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CommunityToolkit.Mvvm.DependencyInjection;
using WinTabber.API.Thumbnails;
using WinTabberUI.Services;
using WinTabberUI.ViewModels;
using Windows.Win32.Foundation;

namespace WinTabberUI;

/// <summary>
/// Chromeless (aside from a slim custom title bar), resizable floating window that hosts a live
/// <see cref="WindowThumbnail"/> preview of a window that has been moved off-screen by
/// <see cref="IWindowThumbnailService"/>. Multi-instance: one per thumbnailed window, created transiently
/// (no <c>ViewCoordinatorBase&lt;T&gt;</c>, which only manages a single shared instance).
///
/// How resizing this window behaves is governed by <see cref="ThumbnailResizeMode"/> (Settings → General):
/// <list type="bullet">
/// <item><see cref="ThumbnailResizeMode.ThumbOnlyLockedAspect"/> — this window's own resize is locked to the
/// source's original aspect ratio (via WM_SIZING), so it's effectively a single scale factor. The real
/// window is never touched.</item>
/// <item><see cref="ThumbnailResizeMode.ThumbOnlyFreeAspect"/> — this window can be resized to any aspect
/// ratio. The real window is still never touched.</item>
/// <item><see cref="ThumbnailResizeMode.ResizeSource"/> — same free resize, but the real (off-screen) window
/// is also resized, once per drag-release, by a uniform zoom factor computed from how much this window was
/// resized relative to its starting size (preserving the source's own aspect ratio even if this window's
/// was stretched non-uniformly).</item>
/// </list>
/// In every mode, <see cref="WindowThumbnail"/>'s Stretch mode scales the live DWM bitmap to fill whatever
/// size this window ends up being — a pure optical zoom while dragging. Real resizes (when enabled) only
/// ever happen once a drag ends (WM_EXITSIZEMOVE — the Win32 "a size/move drag was just released" message;
/// it doesn't fire for our own programmatic SetWindowPos calls to the *source* window, so there's no
/// feedback loop) or when this window closes — never on every intermediate frame of a drag, which would be
/// needlessly expensive and would reflow the target app's content constantly.
/// </summary>
public partial class ThumbnailWindow : Window
{
    private const int WM_SIZING = 0x0214;
    private const int WM_EXITSIZEMOVE = 0x0232;

    private readonly IWindowThumbnailService _thumbnailService;
    private readonly SettingsViewModel _settings;
    private IDisposable? _serviceWatch;
    private HwndSource? _hwndSource;
    private bool _closingFromService;
    private int _handle;
    private int _originalWidth;
    private int _originalHeight;

    public ThumbnailWindow()
    {
        InitializeComponent();
        _thumbnailService = Ioc.Default.GetRequiredService<IWindowThumbnailService>();
        _settings = Ioc.Default.GetRequiredService<SettingsViewModel>();
        DataContext = Ioc.Default.GetRequiredService<ThumbnailWindowViewModel>();
        Closing += OnClosing;
    }

    // Named to avoid hiding the inherited Window.ResizeMode property (used by the XAML's
    // ResizeMode="CanResizeWithGrip" for the OS resize-grip behavior — unrelated to this setting).
    private ThumbnailResizeMode ThumbnailZoomMode => _settings.General.ThumbnailResizeMode;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = (HwndSource)PresentationSource.FromVisual(this);
        _hwndSource.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SIZING && ThumbnailZoomMode == ThumbnailResizeMode.ThumbOnlyLockedAspect)
        {
            LockAspectRatio(wParam, lParam);
            handled = true;
            return (IntPtr)1;
        }

        if (msg == WM_EXITSIZEMOVE && ThumbnailZoomMode == ThumbnailResizeMode.ResizeSource)
        {
            ApplyZoomFactor();
        }

        return IntPtr.Zero;
    }

    public ThumbnailWindowViewModel ViewModel => (ThumbnailWindowViewModel)DataContext;

    /// <summary>Wires this window up to a specific thumbnailed window. Must be called once, before <see cref="Window.Show"/>.</summary>
    public void Initialize(int handle, string title, int originalWidth, int originalHeight)
    {
        _handle = handle;
        _originalWidth = originalWidth;
        _originalHeight = originalHeight;
        ViewModel.Initialize(handle, title);
        Thumbnail.Source = handle;

        // If the entry disappears — the source window being destroyed (watchdog self-restore) or app
        // shutdown restoring everything — close this window too. Our own close paths (expand button,
        // Alt+F4, taskbar) all go through OnClosing below instead, which removes the entry itself.
        _serviceWatch = _thumbnailService
            .Connect()
            .ObserveOnDispatcher()
            .Subscribe(_ =>
            {
                if (!_thumbnailService.IsThumbnailed(_handle))
                {
                    _closingFromService = true;
                    Close();
                }
            });
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _serviceWatch?.Dispose();
        _hwndSource?.RemoveHook(WndProc);

        // Covers every close path: the expand button, Alt+F4, taskbar, the OS window-close affordance.
        // Idempotent — a no-op if the entry is already gone (e.g. the source window was destroyed and the
        // watchdog already restored/removed it, in which case _closingFromService is true and there's
        // nothing left to zoom or restore).
        if (!_closingFromService)
        {
            if (ThumbnailZoomMode == ThumbnailResizeMode.ResizeSource)
            {
                ApplyZoomFactor();
            }

            _thumbnailService.StopThumbnail(_handle);
        }
    }

    /// <summary>
    /// Computes how much this window was resized relative to its starting size (the geometric mean of the
    /// width and height ratios, so a non-uniformly stretched preview still yields a single sensible factor)
    /// and applies that as a uniform scale to the source window's original, real dimensions. This keeps the
    /// restored window's own proportions intact even though the preview itself was allowed to stretch freely.
    /// Only called when <see cref="ThumbnailZoomMode"/> is <see cref="ThumbnailResizeMode.ResizeSource"/>.
    /// </summary>
    private void ApplyZoomFactor()
    {
        if (_originalWidth <= 0 || _originalHeight <= 0)
        {
            return;
        }

        double contentWidth = ThumbnailHost.ActualWidth;
        double contentHeight = ThumbnailHost.ActualHeight;
        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        double displayedWidth = contentWidth * dpi.DpiScaleX;
        double displayedHeight = contentHeight * dpi.DpiScaleY;

        double widthRatio = displayedWidth / _originalWidth;
        double heightRatio = displayedHeight / _originalHeight;
        double zoomFactor = Math.Sqrt(widthRatio * heightRatio);

        int newWidth = (int)Math.Round(_originalWidth * zoomFactor);
        int newHeight = (int)Math.Round(_originalHeight * zoomFactor);
        _thumbnailService.Resize(_handle, newWidth, newHeight);
    }

    /// <summary>
    /// Adjusts the proposed WM_SIZING rect so the window's content area (total size minus the header row,
    /// which is always reserved at <see cref="HeaderBar"/>'s fixed height regardless of hover state — see
    /// ThumbnailWindow.xaml) keeps the source's original aspect ratio, making the resize behave like a
    /// single scale factor rather than a free two-dimensional resize. The dragged edge(s) stay
    /// authoritative; the other dimension is derived from them.
    /// </summary>
    private void LockAspectRatio(IntPtr wParam, IntPtr lParam)
    {
        if (_originalWidth <= 0 || _originalHeight <= 0)
        {
            return;
        }

        var rect = Marshal.PtrToStructure<RECT>(lParam);
        var dpi = VisualTreeHelper.GetDpi(this);
        double headerHeightPx = HeaderBar.ActualHeight * dpi.DpiScaleY;
        double contentAspect = (double)_originalWidth / _originalHeight;

        int edge = (int)wParam;
        bool verticalDragOnly = edge is 3 or 6; // WMSZ_TOP, WMSZ_BOTTOM

        if (verticalDragOnly)
        {
            double contentHeight = Math.Max(1, rect.bottom - rect.top - headerHeightPx);
            int newWidth = (int)Math.Round(contentHeight * contentAspect);
            rect.right = rect.left + newWidth;
        }
        else
        {
            double contentWidth = Math.Max(1, rect.right - rect.left);
            int newHeight = (int)Math.Round(contentWidth / contentAspect + headerHeightPx);

            // Corners/edges that don't touch the top edge (LEFT, RIGHT, BOTTOMLEFT, BOTTOMRIGHT) keep the
            // top fixed and grow/shrink from the bottom; the ones that drag the top edge itself (TOPLEFT,
            // TOPRIGHT) keep the bottom fixed instead, since that's the corner/edge NOT being dragged.
            bool anchorTop = edge is 1 or 2 or 7 or 8; // WMSZ_LEFT, WMSZ_RIGHT, WMSZ_BOTTOMLEFT, WMSZ_BOTTOMRIGHT
            if (anchorTop)
            {
                rect.bottom = rect.top + newHeight;
            }
            else
            {
                rect.top = rect.bottom - newHeight;
            }
        }

        Marshal.StructureToPtr(rect, lParam, true);
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private static readonly Duration HeaderFadeDuration = new(TimeSpan.FromMilliseconds(150));

    private void Window_MouseEnter(object sender, MouseEventArgs e) => AnimateHeader(visible: true);

    private void Window_MouseLeave(object sender, MouseEventArgs e) => AnimateHeader(visible: false);

    /// <summary>
    /// Fades HeaderBar's opacity only — its row is always reserved at full height (see ThumbnailWindow.xaml),
    /// so the thumbnail row's size never changes and there's nothing to squeeze.
    /// </summary>
    private void AnimateHeader(bool visible)
    {
        HeaderBar.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation { To = visible ? 1.0 : 0.0, Duration = HeaderFadeDuration }
        );
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
