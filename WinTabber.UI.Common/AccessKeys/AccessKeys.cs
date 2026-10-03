using Microsoft.UI.Xaml;

namespace WinTabber.UI.Common.AccessKeys;

/// <summary>
/// Wraps UIElement.AccessKey so a single declarative value does both jobs a hinted element needs:
/// sets the native key, and registers the element with its window's AccessKeyBadgeLayer so it gets
/// a custom badge instead of the (disabled) default one. Replaces a plain AccessKey="X" attribute
/// in XAML plus a separate imperative badgeLayer.Watch(element) call in code-behind.
/// <para>
/// The Watch registration is deferred to the element's first Loaded, not done inline in the
/// changed callback: XAML-declared Key values are applied during InitializeComponent, before the
/// owning window's AccessKeyBadgeLayer has been constructed (that happens later in the window's
/// own constructor), so no layer is registered yet at that point. IsWatchedProperty guards against
/// registering more than once -- Key can be re-set many times after the first Loaded (e.g. a bound
/// AccessKey that changes with localized hint text), and AccessKeyBadgeLayer.Watch has no
/// built-in dedup against being called twice for the same element.
/// </para>
/// </summary>
public static class AccessKeys
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key",
        typeof(string),
        typeof(AccessKeys),
        new PropertyMetadata(null, OnKeyChanged)
    );

    private static readonly DependencyProperty IsWatchedProperty = DependencyProperty.RegisterAttached(
        "IsWatched",
        typeof(bool),
        typeof(AccessKeys),
        new PropertyMetadata(false)
    );

    public static void SetKey(UIElement element, string value) => element.SetValue(KeyProperty, value);

    public static string GetKey(UIElement element) => (string)element.GetValue(KeyProperty);

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement)d;
        element.AccessKey = (string)e.NewValue ?? string.Empty;

        if ((bool)element.GetValue(IsWatchedProperty))
        {
            // Native key just updated above; the Watch registration itself only ever happens once.
            return;
        }

        if (element is FrameworkElement { IsLoaded: true })
        {
            RegisterWatch(element);
        }
        else if (element is FrameworkElement notYetLoaded)
        {
            notYetLoaded.Loaded += OnLoaded;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs args)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnLoaded;
        RegisterWatch(element);
    }

    private static void RegisterWatch(UIElement element)
    {
        element.SetValue(IsWatchedProperty, true);

        if (element.XamlRoot is { } root && AccessKeyBadgeLayer.TryGetForXamlRoot(root, out var layer))
        {
            layer.Watch(element);
        }
    }
}
