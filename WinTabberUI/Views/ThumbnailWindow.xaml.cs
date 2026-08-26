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
using Windows.Win32;
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
    // Window messages, hit-test codes (HT*) and resize-edge codes (WMSZ_*) all come from CsWin32 via
    // NativeMethods.txt rather than being redeclared here — see Windows.Win32.PInvoke.

    /// <summary>How far *inside* the visible frame's edge still counts as a resize grab, in DIPs.</summary>
    private const double ResizeBandInner = 6;

    /// <summary>
    /// How far *outside* the visible frame's edge (i.e. into the shadow margin) still counts as a resize
    /// grab, in DIPs. Kept below the margin width so the outermost sliver of shadow stays non-interactive.
    /// </summary>
    private const double ResizeBandOuter = 8;

    /// <summary>How far along an edge from a corner still counts as that corner (diagonal resize), in DIPs.</summary>
    private const double ResizeCornerLength = 16;

    /// <summary>
    /// Inward grab depth at the top edge. Much shallower than <see cref="ResizeBandInner"/> because the top
    /// of the visible frame is <see cref="HeaderBar"/>, whose contents (title, expand button) need to stay
    /// clickable rather than turning into a resize cursor.
    /// </summary>
    private const double ResizeBandInnerTop = 2;

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

    // Named to avoid hiding the inherited Window.ResizeMode property (set to ResizeMode="CanResize" in the
    // XAML, which is what gives the window a resize frame at all — unrelated to this setting, which only
    // governs what a resize *does*).
    private ThumbnailResizeMode ThumbnailZoomMode => _settings.General.ThumbnailResizeMode;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = (HwndSource)PresentationSource.FromVisual(this);
        _hwndSource.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == PInvoke.WM_NCHITTEST)
        {
            int hit = HitTestResizeBorder(lParam);
            if (hit != (int)PInvoke.HTCLIENT)
            {
                handled = true;
                return (IntPtr)hit;
            }

            // Fall through to WPF/DefWindowProc so normal client hit-testing (header buttons, DragMove)
            // still works.
            return IntPtr.Zero;
        }

        if (msg == PInvoke.WM_SIZING && ThumbnailZoomMode == ThumbnailResizeMode.ThumbOnlyLockedAspect)
        {
            LockAspectRatio(wParam, lParam);
            handled = true;
            return (IntPtr)1;
        }

        if (msg == PInvoke.WM_EXITSIZEMOVE && ThumbnailZoomMode == ThumbnailResizeMode.ResizeSource)
        {
            ApplyZoomFactor();
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Maps a screen point to a resize-frame hit-test code based on its distance from <see cref="WindowFrame"/>'s
    /// edges — the *visible* bounds — rather than the window's own outer edge.
    ///
    /// The two differ: the window is inflated by <see cref="WindowFrame"/>'s margin so the drop shadow has room
    /// to render (see ThumbnailWindow.xaml), so the OS's default resize frame sits out in empty, invisible space
    /// several pixels away from anything the user can actually see. This puts the grab zone where the edge
    /// *looks* like it is, straddling it: <see cref="ResizeBandOuter"/> DIPs out into the shadow margin and
    /// <see cref="ResizeBandInner"/> DIPs in over the thumbnail.
    ///
    /// Returns <see cref="HTCLIENT"/> to mean "not a resize grab, handle this normally".
    /// </summary>
    private int HitTestResizeBorder(IntPtr lParam)
    {
        // WM_NCHITTEST can arrive before layout has run, when there are no meaningful bounds to test against.
        if (WindowFrame.ActualWidth <= 0 || WindowFrame.ActualHeight <= 0)
        {
            return (int)PInvoke.HTCLIENT;
        }

        // lParam packs two *signed* 16-bit screen coordinates; the sign matters on multi-monitor setups where
        // a secondary display sits left of / above the primary and so has negative coordinates.
        int lp = (int)(long)lParam;
        var screenPoint = new Point(unchecked((short)(lp & 0xFFFF)), unchecked((short)((lp >> 16) & 0xFFFF)));

        Point p;
        try
        {
            // PointFromScreen takes device pixels and yields element-local DIPs, so DPI is handled for us.
            p = WindowFrame.PointFromScreen(screenPoint);
        }
        catch (InvalidOperationException)
        {
            // The visual isn't connected to a presentation source (teardown, or before the HWND is live).
            return (int)PInvoke.HTCLIENT;
        }

        // The header's expand button is right-aligned against the frame's right edge, so the right and
        // top-right bands would otherwise sit on top of it and turn it into a resize handle. The button wins;
        // the corner is still grabbable just outside it, in the shadow margin.
        if (IsOverExpandButton(p))
        {
            return (int)PInvoke.HTCLIENT;
        }

        bool left = p.X >= -ResizeBandOuter && p.X < ResizeBandInner;
        bool right = p.X <= WindowFrame.ActualWidth + ResizeBandOuter && p.X > WindowFrame.ActualWidth - ResizeBandInner;
        bool top = p.Y >= -ResizeBandOuter && p.Y < ResizeBandInnerTop;
        bool bottom =
            p.Y <= WindowFrame.ActualHeight + ResizeBandOuter && p.Y > WindowFrame.ActualHeight - ResizeBandInner;

        // A point can only be in a band if it's also within the frame's extent on the *other* axis (plus the
        // outward slack) — otherwise the diagonal shadow corners, which are outside the frame on both axes,
        // would read as edge grabs.
        bool withinX = p.X >= -ResizeBandOuter && p.X <= WindowFrame.ActualWidth + ResizeBandOuter;
        bool withinY = p.Y >= -ResizeBandOuter && p.Y <= WindowFrame.ActualHeight + ResizeBandOuter;
        if (!withinX || !withinY)
        {
            return (int)PInvoke.HTCLIENT;
        }

        // Corners take priority over edges, and are widened along both edges so the diagonal grab is reachable.
        bool nearLeft = p.X < ResizeCornerLength;
        bool nearRight = p.X > WindowFrame.ActualWidth - ResizeCornerLength;
        bool nearTop = p.Y < ResizeCornerLength;
        bool nearBottom = p.Y > WindowFrame.ActualHeight - ResizeCornerLength;

        if ((left || right || top || bottom) && (nearTop || nearBottom) && (nearLeft || nearRight))
        {
            if (nearTop && nearLeft)
            {
                return (int)PInvoke.HTTOPLEFT;
            }

            if (nearTop && nearRight)
            {
                return (int)PInvoke.HTTOPRIGHT;
            }

            if (nearBottom && nearLeft)
            {
                return (int)PInvoke.HTBOTTOMLEFT;
            }

            return (int)PInvoke.HTBOTTOMRIGHT;
        }

        if (left)
        {
            return (int)PInvoke.HTLEFT;
        }

        if (right)
        {
            return (int)PInvoke.HTRIGHT;
        }

        if (top)
        {
            return (int)PInvoke.HTTOP;
        }

        if (bottom)
        {
            return (int)PInvoke.HTBOTTOM;
        }

        return (int)PInvoke.HTCLIENT;
    }

    /// <summary>
    /// Whether a point (in <see cref="WindowFrame"/>'s coordinate space) lies over <see cref="ExpandButton"/>.
    /// Tested regardless of <see cref="HeaderBar"/>'s opacity, since a zero-opacity element is still
    /// hit-testable in WPF and the button stays clickable when the header is faded out.
    /// </summary>
    private bool IsOverExpandButton(Point pointInFrame)
    {
        if (ExpandButton.ActualWidth <= 0 || ExpandButton.ActualHeight <= 0)
        {
            return false;
        }

        var origin = ExpandButton.TransformToAncestor(WindowFrame).Transform(new Point(0, 0));
        return new Rect(origin, new Size(ExpandButton.ActualWidth, ExpandButton.ActualHeight)).Contains(pointInFrame);
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
        SizeToSourceAspect();

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

    /// <summary>
    /// The header row's reserved height in DIPs, read from the row definition rather than
    /// <see cref="HeaderBar"/>'s ActualHeight so it's available before layout has run (i.e. during
    /// <see cref="Initialize"/>, which the coordinator calls before <see cref="Window.Show"/>).
    /// </summary>
    private double HeaderHeight => HeaderRow.Height.Value;

    /// <summary>Total non-thumbnail width (DIPs): <see cref="WindowFrame"/>'s shadow margin, both sides.</summary>
    private double ChromeWidth => WindowFrame.Margin.Left + WindowFrame.Margin.Right;

    /// <summary>Total non-thumbnail height (DIPs): the shadow margin top and bottom, plus the header row.</summary>
    private double ChromeHeight => WindowFrame.Margin.Top + WindowFrame.Margin.Bottom + HeaderHeight;

    /// <summary>
    /// Sizes the window so its thumbnail area matches the source window's aspect ratio, fitted inside the
    /// default footprint declared in ThumbnailWindow.xaml (so a very wide or very tall source shrinks to fit
    /// rather than opening as an enormous window).
    ///
    /// Without this the window always opened at that fixed default regardless of what it was previewing,
    /// which had two visible consequences: the thumbnail was stretched to a wrong aspect ratio from the
    /// start (<see cref="WindowThumbnail"/> uses Stretch, so it distorts rather than letterboxes), and in
    /// <see cref="ThumbnailResizeMode.ThumbOnlyLockedAspect"/> the very first resize snapped the window onto
    /// the source's aspect ratio — reading as the window abruptly changing shape the moment you grabbed an
    /// edge. Starting at the correct ratio makes that first drag continuous.
    /// </summary>
    private void SizeToSourceAspect()
    {
        if (_originalWidth <= 0 || _originalHeight <= 0)
        {
            return;
        }

        // Width/Height are explicit in the XAML, so they're real numbers here rather than NaN ("size to
        // content") — but NaN would propagate silently into the assignments below, so rule it out.
        if (double.IsNaN(Width) || double.IsNaN(Height))
        {
            return;
        }

        double maxContentWidth = Width - ChromeWidth;
        double maxContentHeight = Height - ChromeHeight;
        if (maxContentWidth <= 0 || maxContentHeight <= 0)
        {
            return;
        }

        // Fit, not fill: whichever axis is the binding constraint decides the scale, so the result never
        // exceeds the default footprint on either axis.
        double scale = Math.Min(maxContentWidth / _originalWidth, maxContentHeight / _originalHeight);

        Width = _originalWidth * scale + ChromeWidth;
        Height = _originalHeight * scale + ChromeHeight;
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
    /// Adjusts the proposed WM_SIZING rect so the window's content area — the total window size minus both
    /// <see cref="WindowFrame"/>'s shadow margin on all four sides and the header row, the latter always
    /// reserved at <see cref="HeaderBar"/>'s fixed height regardless of hover state (see
    /// ThumbnailWindow.xaml) — keeps the source's original aspect ratio, making the resize behave like a
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

        // The WM_SIZING rect covers the whole native window, which is inflated on all four sides by
        // WindowFrame's shadow margin and again at the top by the header row. Only what's left after
        // subtracting both is the thumbnail itself, so the aspect ratio has to be applied to that — and the
        // chrome added back on before writing the rect out. Read from the live margin rather than a literal
        // so this stays correct if ThumbnailWindow.xaml's margin changes.
        double chromeX = ChromeWidth * dpi.DpiScaleX;
        double chromeY = ChromeHeight * dpi.DpiScaleY;
        double contentAspect = (double)_originalWidth / _originalHeight;

        uint edge = (uint)wParam;
        bool verticalDragOnly = edge is PInvoke.WMSZ_TOP or PInvoke.WMSZ_BOTTOM;

        if (verticalDragOnly)
        {
            double contentHeight = Math.Max(1, rect.bottom - rect.top - chromeY);
            int newWidth = (int)Math.Round(contentHeight * contentAspect + chromeX);
            rect.right = rect.left + newWidth;
        }
        else
        {
            double contentWidth = Math.Max(1, rect.right - rect.left - chromeX);
            int newHeight = (int)Math.Round(contentWidth / contentAspect + chromeY);

            // Corners/edges that don't touch the top edge keep the top fixed and grow/shrink from the
            // bottom; the ones that drag the top edge itself (TOPLEFT, TOPRIGHT) keep the bottom fixed
            // instead, since that's the corner/edge NOT being dragged.
            bool anchorTop = edge
                is PInvoke.WMSZ_LEFT
                    or PInvoke.WMSZ_RIGHT
                    or PInvoke.WMSZ_BOTTOMLEFT
                    or PInvoke.WMSZ_BOTTOMRIGHT;
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
