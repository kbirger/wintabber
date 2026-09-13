using Microsoft.UI.Xaml.Data;
using WinTabber.Infrastructure;

namespace WinTabberUI.ValueConverters;

/// <summary>
/// Placeholder per this plan's Global Constraints: every IconKey maps to the same "Help" glyph until
/// Phase 6 does the real IconKey-to-WinUI-glyph mapping. Do not add real per-key glyphs here before
/// Phase 6 — that is the one phase this plan allows to replace this file's placeholder behavior.
/// </summary>
public sealed class IconKeyToGlyphConverter : IValueConverter
{
    // TODO(icon): placeholder for every IconKey; Phase 6 replaces this with real per-key glyphs.
    private const string PlaceholderGlyph = "";

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is IconKey ? PlaceholderGlyph : PlaceholderGlyph;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
