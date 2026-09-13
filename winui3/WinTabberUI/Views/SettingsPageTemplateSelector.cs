using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinTabber.ViewModels.Settings;

namespace WinTabberUI.Views;

/// <summary>
/// The native WinUI 3 replacement for WPF's implicit Frame/ContentControl DataTemplate-by-DataType
/// selection: WinUI 3's Frame is real page navigation with no equivalent, so SettingsWindow uses a
/// plain ContentControl with this selector instead.
/// </summary>
public sealed class SettingsPageTemplateSelector : DataTemplateSelector
{
    // NOTE: not `required` -- WinUI 3's XamlTypeInfo.g.cs metadata generator instantiates this type
    // via its parameterless constructor for XAML type-info purposes (confirmed against real compiler
    // output: CS9035 "required member must be set" from generated code we don't control), so `required`
    // properties cannot be used on any type XAML instantiates this way.
    public DataTemplate AppearanceTemplate { get; set; } = null!;
    public DataTemplate GeneralTemplate { get; set; } = null!;
    public DataTemplate ShortcutsTemplate { get; set; } = null!;

    // IMPORTANT: Microsoft.UI.Xaml.Controls.DataTemplateSelector actually exposes TWO virtual
    // overloads: SelectTemplateCore(object) and SelectTemplateCore(object, DependencyObject).
    // ContentPresenter calls the two-argument overload, and the base implementation of that
    // overload does NOT chain to the one-argument overload (confirmed by reflecting the installed
    // Microsoft.WinUI.dll). Overriding only the one-argument form compiles cleanly, throws no
    // exception, and is simply never invoked -- ContentPresenter silently falls back to rendering
    // Content.ToString() as plain text. The two-argument overload must carry the real logic; the
    // one-argument overload delegates to it so any other caller of the single-arg form still works.
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateFor(item);

    protected override DataTemplate SelectTemplateCore(object item) => SelectTemplateFor(item);

    private DataTemplate SelectTemplateFor(object item) =>
        item switch
        {
            AppearanceSettingsViewModel => AppearanceTemplate,
            GeneralSettingsViewModel => GeneralTemplate,
            ShortcutsSettingsViewModel => ShortcutsTemplate,
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, "No template registered for this settings section."),
        };
}
