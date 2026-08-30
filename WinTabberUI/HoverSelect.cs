using System.Windows;

namespace WinTabberUI;

/// <summary>
/// Gates the selector's hover-to-select behaviour on the pointer having actually moved.
/// <para>
/// The selector opens centred on whichever screen the cursor is on, so the reveal drops tiles
/// underneath a stationary pointer. WPF re-hit-tests when a window appears and raises MouseEnter
/// without the mouse having moved at all, which fired the tile's <c>IsMouseOver</c> trigger and
/// pulled the selection off the item the view model had chosen. That is the selection "jump" seen
/// on open: the first frame paints the view model's choice, the next paints whatever happened to
/// be under the cursor.
/// </para>
/// <para>
/// The property is inheritable, so setting it on the list reaches every generated container -
/// which a <c>MultiTrigger</c> condition needs, since conditions cannot take a binding.
/// <see cref="SpatialNavigationListView" /> owns the flag: it clears it as the selector is shown
/// and sets it again at the first genuine mouse movement.
/// </para>
/// </summary>
public static class HoverSelect
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(HoverSelect),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
}
