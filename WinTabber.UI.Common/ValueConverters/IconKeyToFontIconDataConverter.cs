using System.Globalization;
using System.Windows.Data;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using WinTabber.Infrastructure;

namespace WinTabber.UI.Common.ValueConverters;

/// <summary>
/// Maps the UI-framework-agnostic <see cref="IconKey"/> to the iNKORE glyph the WPF app renders
/// today. This is the only place in the WPF app allowed to translate an IconKey — everywhere else
/// (ViewModels, Infrastructure) is deliberately unaware that iNKORE exists.
/// </summary>
public sealed class IconKeyToFontIconDataConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IconKey key)
        {
            return Binding.DoNothing;
        }

        return key switch
        {
            IconKey.ArrowNext_24_Filled => FluentSystemIcons.ArrowNext_24_Filled,
            IconKey.ArrowPrevious_24_Filled => FluentSystemIcons.ArrowPrevious_24_Filled,
            IconKey.Checkmark_24_Filled => FluentSystemIcons.Checkmark_24_Filled,
            IconKey.Dock_24_Filled => FluentSystemIcons.Dock_24_Filled,
            IconKey.ArrowMinimize_24_Filled => FluentSystemIcons.ArrowMinimize_24_Filled,
            IconKey.Maximize_24_Filled => FluentSystemIcons.Maximize_24_Filled,
            IconKey.Speaker2_24_Filled => FluentSystemIcons.Speaker2_24_Filled,
            IconKey.Settings_24_Filled => FluentSystemIcons.Settings_24_Filled,
            IconKey.PictureInPicture_24_Filled => FluentSystemIcons.PictureInPicture_24_Filled,
            IconKey.AppsList_24_Filled => FluentSystemIcons.AppsList_24_Filled,
            IconKey.Sleep_24_Filled => FluentSystemIcons.Sleep_24_Filled,
            IconKey.Dismiss_24_Filled => FluentSystemIcons.Dismiss_24_Filled,
            IconKey.PaintBucket_24_Regular => FluentSystemIcons.PaintBucket_24_Regular,
            IconKey.Settings_32_Filled => FluentSystemIcons.Settings_32_Filled,
            IconKey.Keyboard_24_Filled => FluentSystemIcons.Keyboard_24_Filled,
            _ => throw new ArgumentOutOfRangeException(nameof(value), key, "Unmapped IconKey."),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
