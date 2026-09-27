using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    private readonly Dictionary<UIElement, (AccessKeyBadge Badge, Popup Popup)> _badges = new();

    // Each static badge lives in its own Popup, not as a child of the overlay Canvas. A ComboBox
    // drop-down is itself a Popup, and WinUI draws every open Popup in the PopupRoot above all
    // normal window content, so a Canvas child can never draw above it -- confirmed live: this
    // Canvas-child version was the original form of this bug (badges drawing behind an open
    // drop-down). The overlay Canvas stays only as the anchor supplying XamlRoot.
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

        if (_badges.TryGetValue(sender, out var existing) && existing.Badge.Text != sender.AccessKey)
        {
            // sender's AccessKey changed since this badge was created (DynamicAccessKeyScope
            // reassigns a reused ComboBoxItem's key on every DropDownOpened) -- the cached badge's
            // text is now stale and must not be reused with an underline length computed for a
            // different string.
            existing.Popup.IsOpen = false;
            _badges.Remove(sender);
        }

        if (!_badges.TryGetValue(sender, out var entry))
        {
            if (underlineLength < 0)
            {
                // Never shown for this request (the very first character already diverges from
                // this element's key) -- nothing to create or hide.
                return;
            }

            var newBadge = new AccessKeyBadge(sender.AccessKey);
            var popup = new Popup
            {
                XamlRoot = _overlay.XamlRoot,
                Child = newBadge.Visual,
                IsHitTestVisible = false,
                ShouldConstrainToRootBounds = false,
            };
            entry = (newBadge, popup);
            _badges[sender] = entry;
        }

        // Recomputed on every request, not cached, so the badge stays correctly placed even if
        // layout shifts mid-sequence. Matches HintPosition.TopLeft's exact offset (bounds.Left - 4,
        // bounds.Top - 4), ported from WPF's HintPosition.cs. A Popup with a XamlRoot and no parent
        // is positioned in window-content coordinates, which TransformToVisual(null) yields.
        var point = sender.TransformToVisual(null).TransformPoint(new Point(0, 0));
        entry.Popup.HorizontalOffset = point.X - 4;
        entry.Popup.VerticalOffset = point.Y - 4;
        entry.Popup.IsOpen = true;

        entry.Badge.UpdatePrefix(underlineLength);
    }

    private void OnAccessKeyDisplayDismissed(UIElement sender, AccessKeyDisplayDismissedEventArgs args)
    {
        // A dismiss for an element with no active badge (e.g. dismissed twice) is a no-op, not an
        // error -- Dictionary.Remove's bool return makes that the natural shape here.
        if (_badges.Remove(sender, out var entry))
        {
            entry.Popup.IsOpen = false;
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
    /// Snapshots the popups currently open for this layer's XamlRoot -- call this BEFORE a
    /// popup-owning control's own popup opens, so InjectIntoNewPopup can later diff against it to
    /// find the newly opened one. Split into two methods (this one and InjectIntoNewPopup) instead
    /// of one bracketing call, because a control like ComboBox opens its own drop-down
    /// asynchronously, driven by the framework -- by the time DropDownOpened fires, the popup has
    /// ALREADY opened, so a single "snapshot before, run an action, snapshot after" call has
    /// nothing to bracket: both snapshots would already include the new popup (confirmed live: this
    /// was the root cause of item badges never appearing). The caller must snapshot at the true
    /// "about to open" moment (for a ComboBox, AccessKeyInvoked -- which the framework raises
    /// before it opens the drop-down) and inject at the "opened" moment (DropDownOpened).
    /// </summary>
    public IReadOnlyList<Popup> SnapshotOpenPopups(UIElement owner)
    {
        return VisualTreeHelper.GetOpenPopupsForXamlRoot(_overlay.XamlRoot).ToList();
    }

    /// <summary>
    /// Finds the one Popup that opened since <paramref name="before"/> was captured (see
    /// SnapshotOpenPopups) and hands its content Panel to <paramref name="onPopupOpened"/> so item
    /// badges can be added as direct children of it -- sharing the SAME popup as the drop-down, not
    /// a sibling one -- required for them to draw above the drop-down's own content: a ComboBox
    /// drop-down is a light-dismiss Popup, and WinUI keeps light-dismiss popups on a stacking layer
    /// above ordinary Popups regardless of open order, so no sibling Popup can ever draw above it.
    /// Confirmed live via VisualTreeHelper.GetOpenPopupsForXamlRoot.
    ///
    /// Does nothing (onPopupOpened is not called) if no new Popup is found, or if the new Popup's
    /// Child is not a Panel -- both are silent no-ops, not errors.
    /// </summary>
    public void InjectIntoNewPopup(IReadOnlyList<Popup> before, Action<Panel> onPopupOpened)
    {
        var after = VisualTreeHelper.GetOpenPopupsForXamlRoot(_overlay.XamlRoot).ToList();
        var newPopup = FindNewPopup(before, after);

        if (newPopup?.Child is Panel panel)
        {
            onPopupOpened(panel);
        }
    }

    /// <summary>
    /// Creates one AccessKeyBadge for a dynamically-keyed ComboBoxItem and adds it as a direct
    /// child of dropDownPanel -- the drop-down's own Popup content, located by WatchPopupOwner --
    /// rather than through the normal Watch/_badges path, which places a badge in its own sibling
    /// Popup that a ComboBox drop-down (a light-dismiss Popup) always draws above regardless of
    /// open order. Wires the container's own AccessKeyDisplayRequested/Dismissed directly,
    /// bypassing OnAccessKeyDisplayRequested/Dismissed entirely, since those methods assume the
    /// sibling-Popup shape this badge does not use.
    /// </summary>
    public static void AddItemBadge(Panel dropDownPanel, ComboBoxItem container)
    {
        var badge = new AccessKeyBadge(container.AccessKey);
        dropDownPanel.Children.Add(badge.Visual);

        container.AccessKeyDisplayRequested += (sender, args) =>
        {
            var underlineLength = AccessKeyBadge.ComputeUnderlineLength(sender.AccessKey, args.PressedKeys);

            // Position is relative to dropDownPanel, not the window -- container and dropDownPanel
            // share that panel as a common ancestor once the drop-down is open, so
            // TransformToVisual(dropDownPanel) gives coordinates already in the right space, matching
            // the -4/-4 offset the window-level path uses (see OnAccessKeyDisplayRequested).
            var point = container.TransformToVisual(dropDownPanel).TransformPoint(new Point(0, 0));
            Canvas.SetLeft(badge.Visual, point.X - 4);
            Canvas.SetTop(badge.Visual, point.Y - 4);

            badge.UpdatePrefix(underlineLength);
        };

        container.AccessKeyDisplayDismissed += (_, _) =>
        {
            badge.UpdatePrefix(-1);
        };
    }
}
