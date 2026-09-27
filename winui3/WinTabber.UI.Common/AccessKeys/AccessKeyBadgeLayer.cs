using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WinTabber.UI.Common.AccessKeys;

/// <summary>
/// Replaces WinUI 3's default key-tip badge with a custom AccessKeyBadge for every element
/// registered via Watch. AccessKeyDisplayRequested/Dismissed do NOT bubble -- confirmed against
/// metadata (AccessKeyInvokedEventArgs has a Handled property; these two do not) and a worked
/// example's own per-element wiring -- so there is no single attachment point that reaches every
/// hinted descendant the way WPF's HintBehavior had; each element needs its own Watch call.
/// Requires AccessKeyManager.AreKeyTipsEnabled = false to have been set (once, app-wide, at
/// startup) so the framework's own default badge does not also draw -- see
/// docs/superpowers/specs/2026-09-23-access-key-custom-rendering-winui3-design.md.
/// </summary>
public sealed class AccessKeyBadgeLayer
{
    private readonly Canvas _overlay;
    private readonly Dictionary<UIElement, AccessKeyBadge> _badges = new();

    public AccessKeyBadgeLayer(Canvas overlay)
    {
        _overlay = overlay;
    }

    /// <summary>Registers one element to get a custom badge instead of the (disabled) default one.</summary>
    public void Watch(UIElement element)
    {
        element.AccessKeyDisplayRequested += OnAccessKeyDisplayRequested;
        element.AccessKeyDisplayDismissed += OnAccessKeyDisplayDismissed;
    }

    private void OnAccessKeyDisplayRequested(UIElement sender, AccessKeyDisplayRequestedEventArgs args)
    {
        var underlineLength = AccessKeyBadge.ComputeUnderlineLength(sender.AccessKey, args.PressedKeys);

        if (_badges.TryGetValue(sender, out var existingBadge) && existingBadge.Text != sender.AccessKey)
        {
            // sender's AccessKey changed since this badge was created (DynamicAccessKeyScope
            // reassigns a reused ComboBoxItem's key on every DropDownOpened) -- the cached badge's
            // text is now stale and must not be reused with an underline length computed for a
            // different string.
            _overlay.Children.Remove(existingBadge.Visual);
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
            _overlay.Children.Add(badge.Visual);
        }

        // Recomputed on every request, not cached, so the badge stays correctly placed even if
        // layout shifts mid-sequence. Matches HintPosition.TopLeft's exact offset (bounds.Left - 4,
        // bounds.Top - 4), ported from WPF's HintPosition.cs.
        var point = sender.TransformToVisual(_overlay).TransformPoint(new Point(0, 0));
        Canvas.SetLeft(badge.Visual, point.X - 4);
        Canvas.SetTop(badge.Visual, point.Y - 4);

        badge.UpdatePrefix(underlineLength);
    }

    private void OnAccessKeyDisplayDismissed(UIElement sender, AccessKeyDisplayDismissedEventArgs args)
    {
        // A dismiss for an element with no active badge (e.g. dismissed twice) is a no-op, not an
        // error -- Dictionary.Remove's bool return makes that the natural shape here.
        if (_badges.Remove(sender, out var badge))
        {
            _overlay.Children.Remove(badge.Visual);
        }
    }

    /// <summary>
    /// Finds the one item present in <paramref name="after"/> but not in <paramref name="before"/> --
    /// used to identify a ComboBox's own drop-down Popup right after it opens, by diffing the set of
    /// open popups just before and just after DropDownOpened fires. Returns null if nothing new opened
    /// (should not happen when called right after DropDownOpened, but is not this method's job to
    /// assert -- the caller decides what a null means for it).
    /// </summary>
    internal static T? FindNewPopup<T>(IReadOnlyList<T> before, IReadOnlyList<T> after) where T : class
    {
        foreach (var item in after)
        {
            if (!before.Contains(item))
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// Locates a popup-owning control's own drop-down/flyout Popup right after it opens, by
    /// snapshotting the open popups just before <paramref name="popupOwnerOpening"/> runs and diffing
    /// against the open popups right after. Hands the caller that popup's content Panel via
    /// <paramref name="onPopupOpened"/> so item badges can be added as direct children of it -- sharing
    /// the SAME popup as the drop-down, not a sibling one -- which is required for them to draw above
    /// the drop-down's own content: a ComboBox drop-down is a light-dismiss Popup, and WinUI keeps
    /// light-dismiss popups on a stacking layer above ordinary Popups regardless of open order, so no
    /// sibling Popup (however recently opened) can ever draw above it. Confirmed live via
    /// VisualTreeHelper.GetOpenPopupsForXamlRoot: the drop-down's Popup was listed ahead of badge
    /// Popups opened strictly earlier.
    ///
    /// Does nothing (onPopupOpened is not called) if no new Popup is found, or if the new Popup's
    /// Child is not a Panel -- both are silent no-ops, not errors: a badge that fails to place itself
    /// is a visible-but-recoverable gap, not a crash, and future Windows App SDK versions could change
    /// the drop-down's internal Child type without warning (see this method's caller-facing risk note
    /// in the plan/spec).
    /// </summary>
    public void WatchPopupOwner(UIElement owner, Action popupOwnerOpening, Action<Panel> onPopupOpened)
    {
        var before = VisualTreeHelper.GetOpenPopupsForXamlRoot(_overlay.XamlRoot).ToList();

        popupOwnerOpening();

        var after = VisualTreeHelper.GetOpenPopupsForXamlRoot(_overlay.XamlRoot).ToList();
        var newPopup = FindNewPopup(before, after);

        if (newPopup?.Child is Panel panel)
        {
            onPopupOpened(panel);
        }
    }
}
