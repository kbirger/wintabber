using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace WinTabber.UI.Common.AccessKeys;

/// <summary>
/// A chromeless, click-through window covering exactly the monitor its owner currently occupies,
/// used to draw access-key badges above everything in the owner -- including an open ComboBox
/// drop-down, which a Popup-based badge (two prior designs, both abandoned) cannot reliably beat:
/// a drop-down is a light-dismiss Popup that WinUI keeps on a stacking layer above every ordinary
/// Popup regardless of open order, and injecting into the drop-down's own internal panel renders
/// inconsistently live (confirmed: correct in logic every time, wrong on screen some of the time).
/// A separate HWND's z-order is controlled by the window manager, not WinUI's popup layering, so
/// it draws above the owner unconditionally once positioned correctly.
///
/// Created once per owning window, at that window's construction; shown and hidden thereafter, never
/// recreated per chord.
/// </summary>
public sealed class AccessKeyOverlayWindow : WinUIEx.WindowEx
{
    private readonly Microsoft.UI.Xaml.Window _owner;
    private readonly Canvas _canvas = new();

    public AccessKeyOverlayWindow(Microsoft.UI.Xaml.Window owner)
    {
        _owner = owner;

        IsTitleBarVisible = false;
        IsShownInSwitchers = false;
        IsResizable = false;
        IsMinimizable = false;
        IsMaximizable = false;
        // WinUIEx.TransparentTintBackdrop has no TintOpacity/DarkTintOpacity properties (verified
        // via direct inspection of the installed WinUIEx 2.9.3 package's metadata -- only a
        // settable TintColor of type Windows.UI.Color exists). Its parameterless constructor
        // already initializes TintColor to Microsoft.UI.Colors.Transparent (alpha 0), which is
        // exactly the fully-transparent backdrop this window needs, so no further configuration
        // is required.
        SystemBackdrop = new WinUIEx.TransparentTintBackdrop();

        base.Content = _canvas;

        var hwnd = WindowNative.GetWindowHandle(this);
        var ownerHwnd = WindowNative.GetWindowHandle(_owner);
        AccessKeyOverlayInterop.MakeClickThroughAndOwned(hwnd, ownerHwnd);
        AccessKeyOverlayInterop.ClearBorder(new HWND(hwnd));
    }

    public new Canvas Content => _canvas;

    /// <summary>Resizes and repositions this window to exactly cover the owner's current monitor,
    /// then shows it. Recomputed on every show rather than tracked continuously -- badges are only
    /// ever positioned while a chord is actively displaying, which starts from a hidden overlay every
    /// time, so a stale monitor bound while hidden is never observable.</summary>
    public void ShowOverlay()
    {
        // Per the design spec's Error handling section: if the owner's monitor cannot be found (no
        // known repro; DisplayAreaFallback.Nearest already means this only returns null if literally
        // no display area exists on the system), skip showing this request's badge rather than throw
        // out of an event handler -- same silent-degrade precedent as AccessKeyOverlayInterop above.
        var ownerHwnd = WindowNative.GetWindowHandle(_owner);
        var windowId = Win32Interop.GetWindowIdFromWindow(ownerHwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
        if (displayArea is null)
        {
            return;
        }

        AppWindow.MoveAndResize(displayArea.OuterBounds);

        var hwnd = new HWND(WindowNative.GetWindowHandle(this));
        PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);

        AccessKeyOverlayInterop.ClearBorder(hwnd);

        // ShowWindow only changes visibility, not z-order -- the owned-window relationship
        // (GWLP_HWNDPARENT, set in the constructor) keeps this window grouped with its owner but
        // does not guarantee it is brought to the top of that group every time it is shown,
        // especially since the owner (not this window) is what keeps receiving real activation.
        // HWND_TOPMOST, not HWND_TOP: the owner (MediaControlsWindow) sets IsAlwaysOnTop="True"
        // (WS_EX_TOPMOST), confirmed live via its GWL_EXSTYLE. HWND_TOP only reorders within the
        // non-topmost z-order band, which sits entirely below every topmost window -- against a
        // topmost owner, no reordering within that lower band can ever place this window above it.
        // This reverses this plan's original choice of HWND_TOP specifically to avoid a
        // system-wide-topmost overlay, but that concern doesn't apply in practice: the overlay is
        // hidden except while a chord is actively displaying, so there's no window of time in which
        // it would float above anything the user isn't already interacting with. CsWin32 does not
        // generate a named HWND_TOPMOST constant either (same reason as HWND_TOP: a header macro,
        // not a metadata member) -- it is ((HWND)-1), passed here as (HWND)(-1).
        PInvoke.SetWindowPos(
            hwnd,
            (HWND)(-1),
            0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED
        );
    }

    public void HideOverlay()
    {
        var hwnd = new HWND(WindowNative.GetWindowHandle(this));
        PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_HIDE);
    }

    /// <summary>
    /// Converts elementLocal (an element's position in its own window's coordinates, as
    /// TransformToVisual(null) already gives it) into this overlay's own coordinate space. Both
    /// ownerPosition and overlayPosition are each window's AppWindow.Position -- top-left in
    /// physical screen pixels -- so their difference is already in the same physical-pixel space;
    /// dividing by scale converts that difference into the DIPs elementLocal and the overlay's own
    /// Canvas positions are both expressed in.
    /// </summary>
    internal static Point ComputeOverlayLocalPosition(
        Point elementLocal,
        PointInt32 ownerPosition,
        PointInt32 overlayPosition,
        double scale
    )
    {
        var offsetX = (ownerPosition.X - overlayPosition.X) / scale;
        var offsetY = (ownerPosition.Y - overlayPosition.Y) / scale;
        return new Point(elementLocal.X + offsetX, elementLocal.Y + offsetY);
    }
}

/// <summary>
/// Sets the Win32 window-style properties AccessKeyOverlayWindow needs and that WinUI 3 exposes no
/// managed API for: click-through (so the overlay never intercepts a click meant for whatever it
/// visually covers), the owned-window relationship (so the window manager always keeps it above its
/// owner, without floating above unrelated apps the way WinUIEx's IsAlwaysOnTop would), and stripping
/// the residual WS_CAPTION border (WinUIEx's IsTitleBarVisible = false does not clear the underlying
/// GWL_STYLE bits, which otherwise trace a visible 1px line around the screen at full-monitor size).
/// Lives beside AccessKeyOverlayWindow, not in WinTabber.Interop, because it affects only this app's
/// own window -- see this plan's Global Constraints and CLAUDE.md's "Windows Interop" section for why
/// that boundary is drawn where it is.
/// </summary>
internal static class AccessKeyOverlayInterop
{
    public static void MakeClickThroughAndOwned(nint overlayHwnd, nint ownerHwnd)
    {
        // No logging infrastructure exists anywhere in this app (confirmed: grepped for
        // ILogger/Debug.WriteLine usage, found none), so "log and continue" here means degrade
        // silently rather than throw out of a window
        // constructor -- matching AccessKeyBadge.TryGetResource's existing precedent for a
        // non-critical, visually-recoverable failure. Badges still show; only the click-through and
        // above-owner z-order guarantees would be missing.
        try
        {
            var hwnd = new HWND(overlayHwnd);

            var currentExStyle = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            var newExStyle = currentExStyle
                | (int)WINDOW_EX_STYLE.WS_EX_LAYERED
                | (int)WINDOW_EX_STYLE.WS_EX_TRANSPARENT
                | (int)WINDOW_EX_STYLE.WS_EX_NOACTIVATE;
            PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, newExStyle);

            // GWLP_HWNDPARENT carries a full pointer-sized HWND value -- SetWindowLong's 32-bit
            // signature would truncate it on x64, so this one call needs the pointer-width API.
            PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_HWNDPARENT, ownerHwnd);
        }
        catch (Exception)
        {
            // Intentionally swallowed -- see the comment above this try block.
        }
    }

    public static void ClearBorder(HWND hwnd)
    {
        try
        {
            var currentStyle = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
            var newStyle = currentStyle & ~(int)WINDOW_STYLE.WS_CAPTION;
            PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE, newStyle);
        }
        catch (Exception)
        {
            // Intentionally swallowed -- same silent-degrade rationale as MakeClickThroughAndOwned.
        }
    }
}
