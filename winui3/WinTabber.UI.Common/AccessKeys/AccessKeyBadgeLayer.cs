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
    private readonly Window _owner;
    private readonly AccessKeyOverlayWindow _overlay;
    private readonly Dictionary<UIElement, AccessKeyBadge> _badges = new();

    public AccessKeyBadgeLayer(Window owner)
    {
        _owner = owner;
        _overlay = new AccessKeyOverlayWindow(owner);
    }

    /// <summary>Registers one element to get a custom badge instead of the (disabled) default one.</summary>
    public void Watch(UIElement element)
    {
        element.AccessKeyDisplayRequested += OnAccessKeyDisplayRequested;
        element.AccessKeyDisplayDismissed += OnAccessKeyDisplayDismissed;
    }

    private void OnAccessKeyDisplayRequested(UIElement sender, Microsoft.UI.Xaml.Input.AccessKeyDisplayRequestedEventArgs args)
    {
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
