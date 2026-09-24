using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
}
