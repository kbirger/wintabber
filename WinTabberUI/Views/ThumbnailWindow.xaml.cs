using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Win32.Foundation;
using WinRT.Interop;
using WinTabber.Api.Windowing.Thumbnails;
using WinTabber.ViewModels;
using WinTabberUI.Models.Settings;
using WinTabberUI.Services;
using WinTabberUI.Windowing;
using WinUIEx.Messaging;

namespace WinTabberUI.Views;

/// <summary>
/// Chromeless, resizable floating window that hosts a live <see cref="WinTabberUI.Controls.WindowThumbnail"/>
/// preview of a window that has been moved off-screen by <see cref="IWindowThumbnailService"/>. Multi-instance:
/// one per thumbnailed window, created transient (Bootstrapper registers it <c>AddTransient</c>, matching every
/// other WinUI 3 Window in this migration -- a Window can only be shown once).
///
/// Ported from the WPF original's <c>ThumbnailWindow.xaml.cs</c>. Two structural differences from that port,
/// both DEVIATIONS worth flagging for whoever touches this file next:
/// <list type="bullet">
/// <item>
/// WPF's <c>HwndSource.AddHook</c> has no WinUI 3 equivalent. This port uses
/// <see cref="WinUIEx.Messaging.WindowMessageMonitor"/> instead -- confirmed present with a settable
/// <c>Handled</c>/<c>Result</c> pair (not just a read-only monitor) via this project's own installed
/// WinUIEx 2.2.0 XML docs, not a guess: <c>WindowMessageEventArgs.Handled</c>/<c>.Result</c> exist and are
/// documented as "set this to set the return result, after also setting Handled to true" -- exactly the
/// override capability <c>WM_NCHITTEST</c> needs. No hand-rolled <c>SetWindowSubclass</c> CsWin32 bindings
/// were needed at all, resolving the plan's stated research blocker for this window a different way than
/// expected.
/// </item>
/// <item>
/// <c>FrameworkElement</c> has no <c>Effect</c> property in WinUI 3, so <c>DropShadowEffect</c> has no
/// drop-in replacement (a composition-shadow alternative was not researched here). The WPF original's 14px
/// shadow margin, its near-zero-alpha outer wrapper, and the whole margin-relative hit-test band math that
/// existed only to serve that margin are all dropped along with it -- this window now resizes via its own
/// native WS_THICKFRAME border (<c>IsResizable="True"</c>) instead. A DISCLOSED, reversible cosmetic
/// regression: no drop shadow, and resize grabs the literal window edge rather than a few DIPs outside it.
/// Flag for a follow-up if this reads as a real loss once seen live, not just theorized.
/// </item>
/// </list>
/// </summary>
public sealed partial class ThumbnailWindow : WinUIEx.WindowEx
{
    // Declared as literals, not WinUIEx.Messaging.WindowsMessages: that enum is internal to WinUIEx
    // (confirmed by a real compiler error: CS0122 "inaccessible due to its protection level"), and
    // doesn't declare WM_NCHITTEST at all anyway (confirmed by enumerating its XML docs). Values match
    // WPF's own PInvoke.WM_* constants for the same messages.
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_SIZING = 0x0214;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;

    /// <summary>How close to an edge, in DIPs, still counts as "let the native resize border handle it"
    /// rather than claiming the point for HTCAPTION drag. Matches WS_THICKFRAME's own default border
    /// thickness closely enough that this window's whole client area outside that band is draggable.</summary>
    private const double ResizeBorderBand = 8;

    private readonly IWindowThumbnailService _thumbnailService;
    private readonly ApplicationSettings _settings;
    private readonly nint _hwnd;
    private readonly WindowMessageMonitor _messageMonitor;
    private IDisposable? _serviceWatch;
    private bool _closingFromService;
    private int _handle;
    private int _originalWidth;
    private int _originalHeight;

    public ThumbnailWindowViewModel ViewModel { get; }

    // DEVIATION from the WPF original's constructor shape (SettingsViewModel): takes ApplicationSettings
    // directly instead, matching WindowSelectorWindow's own established deviation (Task 4b.4) -- resolves
    // against the real, already-DI-registered singleton rather than threading the whole SettingsViewModel
    // through for one property read.
    public ThumbnailWindow(
        IWindowThumbnailService thumbnailService,
        ApplicationSettings settings,
        ThumbnailWindowViewModel viewModel)
    {
        _thumbnailService = thumbnailService;
        _settings = settings;
        ViewModel = viewModel;

        InitializeComponent();
        AppWindow.SetIcon(DesktopHelper.AppIconPath);

        _hwnd = WindowNative.GetWindowHandle(this);

        // Per the design spec's backdrop table and every other ported window: WindowEx + DesktopAcrylicBackdrop,
        // set in code-behind, not XAML -- a `SystemBackdrop="{winuiex:...}"`-style XAML attribute crashes this
        // SDK's XamlCompiler pass2 with no diagnostic (see SettingsWindow.xaml.cs's comment for the confirmed
        // repro). Acrylic (rather than no backdrop) is a knowing choice here, not an oversight: the WPF original
        // deliberately avoided any blur-behind backdrop so its invisible header strip stayed truly invisible --
        // this port accepts a faint acrylic band showing through the "hidden" header instead of researching a
        // true per-pixel-transparent WinUI 3 window (DwmEnableBlurBehindWindow + a fully-transparent composition
        // brush; a working technique exists but is reported to visibly break in light theme without further
        // work). Disclosed, reversible cosmetic deviation -- see the class doc comment.
        SystemBackdrop = new DesktopAcrylicBackdrop();

        _messageMonitor = new WindowMessageMonitor(this);
        _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;

        RootGrid.PointerEntered += (_, _) => AnimateHeader(visible: true);
        RootGrid.PointerExited += (_, _) => AnimateHeader(visible: false);

        Closed += OnClosed;
    }

    // Named to avoid any confusion with WindowEx's own resize-related surface -- unrelated to this setting,
    // which only governs what a resize *does* once it happens.
    private ThumbnailResizeMode ThumbnailZoomMode => _settings.General.ThumbnailResizeMode;

    private void OnWindowMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        var messageId = (int)e.Message.MessageId;

        if (messageId == WM_NCHITTEST)
        {
            var hit = HitTestDrag(e.Message.LParam);
            if (hit != HTCLIENT)
            {
                e.Handled = true;
                e.Result = hit;
            }
            return;
        }

        if (messageId == WM_SIZING && ThumbnailZoomMode == ThumbnailResizeMode.ThumbOnlyLockedAspect)
        {
            LockAspectRatio(e.Message.WParam, e.Message.LParam);
            e.Handled = true;
            e.Result = 1;
            return;
        }

        if (messageId == WM_EXITSIZEMOVE && ThumbnailZoomMode == ThumbnailResizeMode.ResizeSource)
        {
            ApplyZoomFactor();
        }
    }

    /// <summary>
    /// Maps a screen point to HTCAPTION (drag the window) or HTCLIENT (let native/default processing --
    /// including the WS_THICKFRAME resize border and ExpandButton's own click handling -- take it) based on
    /// distance from this window's own client edges. Unlike the WPF original, there is no shadow margin to
    /// measure against: RootGrid's bounds ARE the window's visible bounds.
    /// </summary>
    private int HitTestDrag(nint lParam)
    {
        if (RootGrid.ActualWidth <= 0 || RootGrid.ActualHeight <= 0)
        {
            return HTCLIENT;
        }

        // lParam packs two *signed* 16-bit screen coordinates, in physical pixels (matching WM_NCHITTEST's
        // documented contract, same as the WPF original's own unpacking) -- not DIPs, so converting to a
        // RootGrid-local point needs both the window's own physical screen position (AppWindow.Position)
        // and the DPI scale, not a visual-tree transform (RootGrid has no visual-tree ancestor to
        // transform against here; it effectively IS the window's content root).
        int lp = (int)lParam;
        var screenPoint = new Windows.Foundation.Point(unchecked((short)(lp & 0xFFFF)), unchecked((short)((lp >> 16) & 0xFFFF)));

        var scale = DesktopHelper.GetScaleForWindow(_hwnd);
        var position = AppWindow.Position;
        var p = new Windows.Foundation.Point(
            screenPoint.X / scale - position.X / scale,
            screenPoint.Y / scale - position.Y / scale);

        bool nearEdge =
            p.X < ResizeBorderBand
            || p.X > RootGrid.ActualWidth - ResizeBorderBand
            || p.Y < ResizeBorderBand
            || p.Y > RootGrid.ActualHeight - ResizeBorderBand;
        if (nearEdge)
        {
            return HTCLIENT;
        }

        if (IsOverExpandButton(p))
        {
            return HTCLIENT;
        }

        return HTCAPTION;
    }

    private bool IsOverExpandButton(Windows.Foundation.Point pointInRoot)
    {
        if (ExpandButton.ActualWidth <= 0 || ExpandButton.ActualHeight <= 0)
        {
            return false;
        }

        var origin = ExpandButton.TransformToVisual(RootGrid).TransformPoint(new Windows.Foundation.Point(0, 0));
        return new Windows.Foundation.Rect(origin, new Windows.Foundation.Size(ExpandButton.ActualWidth, ExpandButton.ActualHeight))
            .Contains(pointInRoot);
    }

    /// <summary>Wires this window up to a specific thumbnailed window. Must be called once, before <see cref="WindowEx.Show"/>.</summary>
    public void Initialize(int handle, string title, int originalWidth, int originalHeight)
    {
        _handle = handle;
        _originalWidth = originalWidth;
        _originalHeight = originalHeight;
        ViewModel.Initialize(handle, title);

        // REAL BUG found via live verification: the thumbnail never appeared at all. Root cause,
        // the exact same class already diagnosed and fixed for WindowSelectorWindow this session:
        // WindowThumbnail.InitialiseThumbnail's `TargetWindow is { } window` guard is always false
        // without this, so DwmRegisterThumbnail never runs. Setting Source alone is not enough --
        // there is no WinUI 3 equivalent of WPF's HwndSource.FromVisual(this) auto-discovery, so the
        // hosting window has to be wired explicitly. Safe to set before Loaded has fired (WindowThumbnail's
        // own DependencyProperty callbacks gate actual registration on _isLoaded internally).
        Thumbnail.TargetWindow = this;
        Thumbnail.Source = handle;
        SizeToSourceAspect();

        // If the entry disappears -- the source window being destroyed (watchdog self-restore) or app
        // shutdown restoring everything -- close this window too. Our own close paths (expand button,
        // taskbar) all go through OnClosed below instead, which removes the entry itself.
        _serviceWatch = _thumbnailService
            .Connect()
            .ObserveOn(ReactiveUI.RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                if (!_thumbnailService.IsThumbnailed(_handle))
                {
                    _closingFromService = true;
                    Close();
                }
            });
    }

    private double HeaderHeight => RootGrid.RowDefinitions[0].Height.Value;

    /// <summary>
    /// Sizes the window so its thumbnail area matches the source window's aspect ratio, fitted inside the
    /// default footprint declared in ThumbnailWindow.xaml (so a very wide or very tall source shrinks to fit
    /// rather than opening as an enormous window). See the WPF original for the full rationale -- unchanged
    /// here except for using AppWindow.ResizeClient (the established WinUI 3 pattern, e.g.
    /// SuspendedWindowsWindow's ResizeToContent) instead of setting Width/Height directly.
    /// </summary>
    private void SizeToSourceAspect()
    {
        if (_originalWidth <= 0 || _originalHeight <= 0)
        {
            return;
        }

        var scale = DesktopHelper.GetScaleForWindow(_hwnd);
        double maxContentWidth = Width * scale;
        double maxContentHeight = (Height - HeaderHeight) * scale;
        if (maxContentWidth <= 0 || maxContentHeight <= 0)
        {
            return;
        }

        // Fit, not fill: whichever axis is the binding constraint decides the scale, so the result never
        // exceeds the default footprint on either axis.
        double contentScale = Math.Min(maxContentWidth / _originalWidth, maxContentHeight / _originalHeight);

        var newContentWidth = _originalWidth * contentScale;
        var newContentHeight = _originalHeight * contentScale + HeaderHeight * scale;

        AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(
            (int)Math.Ceiling(newContentWidth),
            (int)Math.Ceiling(newContentHeight)));
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _serviceWatch?.Dispose();
        _messageMonitor.WindowMessageReceived -= OnWindowMessageReceived;
        _messageMonitor.Dispose();

        // Covers every close path: the expand button, Alt+F4, taskbar, the OS window-close affordance.
        // Idempotent -- a no-op if the entry is already gone (e.g. the source window was destroyed and the
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
    /// and applies that as a uniform scale to the source window's original, real dimensions. Only called
    /// when <see cref="ThumbnailZoomMode"/> is <see cref="ThumbnailResizeMode.ResizeSource"/>.
    /// <para>
    /// KNOWN OPEN ISSUE found via live verification (not yet root-caused): after actually dragging the
    /// preview to a new size, the restored source window lands smaller than expected -- proportionally
    /// closer than before the Task 4b.4-class TargetWindow bug was fixed elsewhere in this file (which
    /// also fixed the thumbnail never rendering at all), but still measurably off. The dimensional
    /// analysis of this method's DIP-to-physical-pixel conversion looks internally consistent, so the
    /// likely suspects are elsewhere: possibly Thumbnail.ActualWidth/ActualHeight not reflecting the
    /// same content-area geometry SizeToSourceAspect assumed when it originally sized the window, or a
    /// rounding/timing issue in when this reads those values relative to the just-finished drag. A
    /// STATUS_STOWED_EXCEPTION native crash (the same crash class documented on WireRealizedTiles in
    /// WindowSelectorWindow.xaml.cs) was also observed after a resize-then-close sequence in this mode
    /// during the same live-verification session, though a definitive managed stack trace tying it to
    /// this method specifically was not captured (the one crash dump obtained showed only an unrelated,
    /// benign first-chance exception). Deferred by explicit user instruction rather than chased further
    /// here -- next step if picked back up should be a fresh procdump run configured for unhandled-only
    /// exceptions (not first-chance), captured during an actual repro rather than opportunistically.
    /// </para>
    /// </summary>
    private void ApplyZoomFactor()
    {
        if (_originalWidth <= 0 || _originalHeight <= 0)
        {
            return;
        }

        double contentWidth = Thumbnail.ActualWidth;
        double contentHeight = Thumbnail.ActualHeight;
        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return;
        }

        var scale = DesktopHelper.GetScaleForWindow(_hwnd);
        double displayedWidth = contentWidth * scale;
        double displayedHeight = contentHeight * scale;

        double widthRatio = displayedWidth / _originalWidth;
        double heightRatio = displayedHeight / _originalHeight;
        double zoomFactor = Math.Sqrt(widthRatio * heightRatio);

        int newWidth = (int)Math.Round(_originalWidth * zoomFactor);
        int newHeight = (int)Math.Round(_originalHeight * zoomFactor);
        _thumbnailService.Resize(_handle, newWidth, newHeight);
    }

    /// <summary>
    /// Adjusts the proposed WM_SIZING rect so the window's content area -- the total window size minus the
    /// header row (the shadow margin the WPF original also subtracted here no longer exists in this port) --
    /// keeps the source's original aspect ratio, making the resize behave like a single scale factor rather
    /// than a free two-dimensional resize. The dragged edge(s) stay authoritative; the other dimension is
    /// derived from them.
    /// </summary>
    private void LockAspectRatio(nuint wParam, nint lParam)
    {
        if (_originalWidth <= 0 || _originalHeight <= 0)
        {
            return;
        }

        var rect = Marshal.PtrToStructure<RECT>(lParam);
        var scale = DesktopHelper.GetScaleForWindow(_hwnd);

        double chromeY = HeaderHeight * scale;
        double contentAspect = (double)_originalWidth / _originalHeight;

        uint edge = (uint)wParam;
        bool verticalDragOnly = edge is 3 or 6; // WMSZ_TOP, WMSZ_BOTTOM

        if (verticalDragOnly)
        {
            double contentHeight = Math.Max(1, rect.bottom - rect.top - chromeY);
            int newWidth = (int)Math.Round(contentHeight * contentAspect);
            rect.right = rect.left + newWidth;
        }
        else
        {
            double contentWidth = Math.Max(1, rect.right - rect.left);
            int newHeight = (int)Math.Round(contentWidth / contentAspect + chromeY);

            // Corners/edges that don't touch the top edge keep the top fixed and grow/shrink from the
            // bottom; the ones that drag the top edge itself (TOPLEFT=4, TOPRIGHT=5) keep the bottom fixed
            // instead, since that's the corner/edge NOT being dragged. WMSZ_* values: LEFT=1, RIGHT=2,
            // TOP=3, BOTTOM=6, TOPLEFT=4, TOPRIGHT=5, BOTTOMLEFT=7, BOTTOMRIGHT=8.
            bool anchorTop = edge is 1 or 2 or 7 or 8;
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

    private static readonly Duration HeaderFadeDuration = new(TimeSpan.FromMilliseconds(150));

    private void AnimateHeader(bool visible)
    {
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = visible ? 1.0 : 0.0,
            Duration = HeaderFadeDuration,
        };
        Storyboard.SetTarget(animation, HeaderBar);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
