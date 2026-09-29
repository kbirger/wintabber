using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
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
/// inconsistently live (confirmed: correct in logic every time, wrong on screen some of the time --
/// see docs/superpowers/plans/2026-09-27-access-key-popup-zorder-fix.md's ledger). A separate HWND's
/// z-order is controlled by the window manager, not WinUI's popup layering, so it draws above the
/// owner unconditionally once positioned correctly. See
/// docs/superpowers/specs/2026-09-28-access-key-overlay-window-design.md for the full design.
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

        base.Content = _canvas;

        var hwnd = WindowNative.GetWindowHandle(this);
        var ownerHwnd = WindowNative.GetWindowHandle(_owner);
        AccessKeyOverlayInterop.MakeClickThroughAndOwned(hwnd, ownerHwnd);
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
        AppWindow.Show();
    }

    public void HideOverlay()
    {
        AppWindow.Hide();
    }
}

/// <summary>
/// Sets the two Win32 extended-window-style properties AccessKeyOverlayWindow needs and that WinUI 3
/// exposes no managed API for: click-through (so the overlay never intercepts a click meant for
/// whatever it visually covers) and the owned-window relationship (so the window manager always
/// keeps it above its owner, without floating above unrelated apps the way WinUIEx's IsAlwaysOnTop
/// would). Lives beside AccessKeyOverlayWindow, not in WinTabber.Interop, because it affects only
/// this app's own window -- see this plan's Global Constraints and CLAUDE.md's "Windows Interop"
/// section for why that boundary is drawn where it is.
/// </summary>
internal static class AccessKeyOverlayInterop
{
    public static void MakeClickThroughAndOwned(nint overlayHwnd, nint ownerHwnd)
    {
        // Per the design spec's Error handling section: no logging infrastructure exists anywhere
        // in winui3/ today (confirmed: grepped for ILogger/Debug.WriteLine usage, found none), so
        // "log and continue" here means degrade silently rather than throw out of a window
        // constructor -- matching AccessKeyBadge.TryGetResource's existing precedent for a
        // non-critical, visually-recoverable failure. Badges still show; only the click-through and
        // above-owner z-order guarantees would be missing.
        try
        {
            var hwnd = new HWND(overlayHwnd);

            var currentExStyle = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            var newExStyle = currentExStyle
                | (nint)WINDOW_EX_STYLE.WS_EX_LAYERED
                | (nint)WINDOW_EX_STYLE.WS_EX_TRANSPARENT
                | (nint)WINDOW_EX_STYLE.WS_EX_NOACTIVATE;
            PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, newExStyle);

            PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_HWNDPARENT, ownerHwnd);
        }
        catch (Exception)
        {
            // Intentionally swallowed -- see the comment above this try block.
        }
    }
}
