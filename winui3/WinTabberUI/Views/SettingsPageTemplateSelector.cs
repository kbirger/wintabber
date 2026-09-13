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

    protected override DataTemplate SelectTemplateCore(object item)
    {
        return item switch
        {
            AppearanceSettingsViewModel => AppearanceTemplate,
            GeneralSettingsViewModel => GeneralTemplate,
            ShortcutsSettingsViewModel => ShortcutsTemplate,
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, "No template registered for this settings section."),
        };
    }
}
