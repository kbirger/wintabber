using Microsoft.UI.Xaml.Data;
using WinTabber.Infrastructure;

namespace WinTabberUI.ValueConverters;

/// <summary>
/// Maps the UI-framework-agnostic <see cref="IconKey"/> to the Segoe Fluent Icons glyph this app
/// renders, mirroring <c>WinTabber.UI.Common.ValueConverters.IconKeyToFontIconDataConverter</c>'s
/// role for the WPF app (that file maps each key to an iNKORE <c>FluentSystemIcons</c> glyph; this
/// one maps the same key to the nearest Segoe Fluent Icons codepoint, since WinUI 3 has no iNKORE
/// dependency). Picks favor an exact or close semantic match to the WPF original's icon; where none
/// existed, the choice is noted below.
/// </summary>
public sealed class IconKeyToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is IconKey key ? ToGlyph(key) : throw new ArgumentException("Expected an IconKey.", nameof(value));

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    /// <summary>
    /// The same mapping as <see cref="Convert"/>, callable directly from an x:Bind function
    /// expression. SettingsWindow.xaml's category icons need this form: SettingsWindow's root is a
    /// Window, not a FrameworkElement, so an x:Bind with `Converter={StaticResource ...}` fails to
    /// compile there (see that file's own comment on SetConverterLookupRoot); a classic
    /// `{Binding Icon, Converter=...}` compiles but never actually applied the converter's output —
    /// not fully root-caused, not worth pursuing further given this working alternative. A static
    /// x:Bind function call goes through neither code path.
    /// </summary>
    public static string ToGlyph(IconKey key) =>
        key switch
        {
            IconKey.ArrowNext_24_Filled => "", // Next
            IconKey.ArrowPrevious_24_Filled => "", // Previous
            IconKey.Checkmark_24_Filled => "", // Accept/checkmark
            IconKey.Dock_24_Filled => "", // DockLeft — no generic "Dock" glyph exists
            // No dedicated minimize/maximize glyphs in the Segoe Fluent Icons Symbol set; these
            // are the same well-known codepoints Windows itself uses for caption buttons.
            IconKey.ArrowMinimize_24_Filled => "",
            IconKey.Maximize_24_Filled => "",
            IconKey.Speaker2_24_Filled => "", // Volume
            IconKey.Settings_24_Filled => "", // Setting
            IconKey.PictureInPicture_24_Filled => "", // same glyph as the tile's floating-thumbnail button
            IconKey.AppsList_24_Filled => "", // AllApps
            IconKey.Sleep_24_Filled => "", // same glyph as the tile's sleep/suspend button
            IconKey.Dismiss_24_Filled => "", // X/close
            IconKey.PaintBucket_24_Regular => "", // Highlight — no paint-bucket glyph in the Symbol set
            IconKey.Settings_32_Filled => "", // Setting — same glyph as Settings_24_Filled, larger in WPF only
            IconKey.Keyboard_24_Filled => "", // Keyboard
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unmapped IconKey."),
        };
}
