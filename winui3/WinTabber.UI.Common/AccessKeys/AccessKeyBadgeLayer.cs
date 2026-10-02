using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace WinTabber.UI.Common.AccessKeys;

/// <summary>
/// Replaces WinUI 3's default key-tip badge with a custom AccessKeyBadge for every element
/// registered via Watch, drawn on a separate AccessKeyOverlayWindow rather than inside this layer's
/// own window -- see docs/superpowers/specs/2026-09-28-access-key-overlay-window-design.md for why:
/// neither a plain Canvas child nor a sibling Popup can reliably draw above an open ComboBox
/// drop-down. AccessKeyDisplayRequested/Dismissed do NOT bubble -- confirmed against metadata
/// (AccessKeyInvokedEventArgs has a Handled property; these two do not) -- so there is no single
/// attachment point that reaches every hinted descendant; each element needs its own Watch call.
/// Requires AccessKeyManager.AreKeyTipsEnabled = false to have been set (once, app-wide, at startup)
/// so the framework's own default badge does not also draw.
/// </summary>
public sealed class AccessKeyBadgeLayer
{
    // Keyed on XamlRoot, not the owning Window directly: it is what the AccessKeys attached
    // property (an arbitrary descendant element, not the window) has at hand to find "this
    // element's window's layer" without a visual-tree walk. Generic registry pulled out separately
    // (AccessKeyBadgeLayerRegistry) so its add/remove/lookup logic is headless-testable.
    private static readonly AccessKeyBadgeLayerRegistry<XamlRoot, AccessKeyBadgeLayer> Registry = new();

    private readonly Window _owner;
    private readonly AccessKeyOverlayWindow _overlay;
    private readonly Dictionary<UIElement, AccessKeyBadge> _badges = new();

    public event Action? OwnerHidden;

    public AccessKeyBadgeLayer(Window owner)
    {
        _owner = owner;
        _overlay = new AccessKeyOverlayWindow(owner);

        // An owned window is hidden by the window manager when its owner is minimized, but NOT when
        // the owner is hidden via SW_HIDE (how this app's own show/hide toggle works) -- nothing else
        // tells this layer the owner went away, so badges could otherwise linger visible over nothing.
        _owner.AppWindow.Changed += OnOwnerAppWindowChanged;

        var xamlRoot = owner.Content.XamlRoot;
        Registry.Register(xamlRoot, this);
        _owner.Closed += (_, _) => Registry.Unregister(xamlRoot);
    }

    /// <summary>Finds the AccessKeyBadgeLayer owning the window an element belongs to, for the
    /// AccessKeys attached property's deferred (Loaded-time) Watch registration.</summary>
    public static bool TryGetForXamlRoot(XamlRoot root, out AccessKeyBadgeLayer layer) =>
        Registry.TryGet(root, out layer!);

    private void OnOwnerAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (!args.DidVisibilityChange || sender.IsVisible)
        {
            return;
        }

        OwnerHidden?.Invoke();

        foreach (var badge in _badges.Values)
        {
            _overlay.Content.Children.Remove(badge.Visual);
        }
        _badges.Clear();
        _overlay.HideOverlay();
    }

    /// <summary>Registers one element to get a custom badge instead of the (disabled) default one.</summary>
    public void Watch(UIElement element)
    {
        element.AccessKeyDisplayRequested += OnAccessKeyDisplayRequested;
        element.AccessKeyDisplayDismissed += OnAccessKeyDisplayDismissed;
    }

    private void OnAccessKeyDisplayRequested(UIElement sender, Microsoft.UI.Xaml.Input.AccessKeyDisplayRequestedEventArgs args)
    {
        if (!_owner.AppWindow.IsVisible)
        {
            // Refuse every request while the owner is hidden, regardless of what triggered it --
            // confirmed live: closing an open ComboBox drop-down as part of hiding (see Task 13) can
            // itself cause the framework to re-request root-level keys, arriving after the
            // owner-hidden badge-clearing pass already ran, which re-showed the overlay with root
            // badges right after everything was supposed to be gone. Checking IsVisible fresh here,
            // rather than a separate tracked flag, is always accurate and needs no explicit reset
            // when the owner becomes visible again.
            return;
        }

        var underlineLength = AccessKeyBadge.ComputeUnderlineLength(sender.AccessKey, args.PressedKeys);

        if (_badges.TryGetValue(sender, out var existingBadge) && existingBadge.Text != sender.AccessKey)
        {
            // sender's AccessKey changed since this badge was created (DynamicAccessKeyScope
            // reassigns a reused ComboBoxItem's key on every DropDownOpened) -- the cached badge's
            // text is now stale and must not be reused with an underline length computed for a
            // different string.
            _overlay.Content.Children.Remove(existingBadge.Visual);
            _badges.Remove(sender);
        }

        if (!_badges.TryGetValue(sender, out var badge))
        {
            if (underlineLength < 0)
            {
                // Never shown for this request (the very first character already diverges from
                // this element's key) -- nothing to create or hide.
                return;
            }

            badge = new AccessKeyBadge(sender.AccessKey);
            _badges[sender] = badge;
            _overlay.Content.Children.Add(badge.Visual);
        }

        if (_badges.Count == 1)
        {
            // First badge of this chord -- the overlay is currently hidden (it hides itself once
            // empty, in OnAccessKeyDisplayDismissed below). Shown before positioning, not after: the
            // position computed below needs the overlay's own AppWindow.Position, which
            // ShowOverlay's MoveAndResize call is what sets.
            _overlay.ShowOverlay();
        }

        var elementLocal = sender.TransformToVisual(null).TransformPoint(new Point(0, 0));
        var scale = _owner.Content.XamlRoot.RasterizationScale;
        var overlayLocal = AccessKeyOverlayWindow.ComputeOverlayLocalPosition(
            elementLocal,
            _owner.AppWindow.Position,
            _overlay.AppWindow.Position,
            scale
        );

        // Matches HintPosition.TopLeft's exact offset (bounds.Left - 4, bounds.Top - 4), ported from
        // WPF's HintPosition.cs.
        Canvas.SetLeft(badge.Visual, overlayLocal.X - 4);
        Canvas.SetTop(badge.Visual, overlayLocal.Y - 4);

        badge.UpdatePrefix(underlineLength);
    }

    private void OnAccessKeyDisplayDismissed(UIElement sender, Microsoft.UI.Xaml.Input.AccessKeyDisplayDismissedEventArgs args)
    {
        // A dismiss for an element with no active badge (e.g. dismissed twice) is a no-op, not an
        // error -- Dictionary.Remove's bool return makes that the natural shape here.
        if (_badges.Remove(sender, out var badge))
        {
            _overlay.Content.Children.Remove(badge.Visual);
        }

        if (_badges.Count == 0)
        {
            _overlay.HideOverlay();
        }
    }
}
