using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinTabber.UI.Common.ValueConverters;

public class BoolToThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (bool)value ? new Thickness(1) : new Thickness(0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToBrushConverter : IValueConverter
{
    public Brush EditBrush { get; set; } = Brushes.White;
    public Brush ViewBrush { get; set; } = Brushes.Transparent;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (bool)value ? EditBrush : ViewBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => !(bool)value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => !(bool)value;
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (bool)value ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => (Visibility)value == Visibility.Visible;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (bool)value ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => (Visibility)value != Visibility.Visible;
}

public class WindowStateToVisibilityConverter : IValueConverter
{
    public WindowState TargetState { get; set; } = WindowState.Normal;
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (WindowState)value == TargetState ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (Visibility)value == Visibility.Visible ? TargetState : WindowState.Normal;
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value == null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
public class EnumToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() ?? (object)0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Enum.Parse(targetType, (string)value);
}

public class StringToEnumConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Enum.Parse(targetType, (string)value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class EnumValuesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => Enum.GetNames(value.GetType());
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class TimeSpanToFloatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return ((TimeSpan)value).TotalSeconds;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return TimeSpan.FromSeconds((double)value);
    }
}

public class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var ts = (TimeSpan)value;
        if(ts.TotalHours > 1)
        {
            return ts.ToString(@"dd\:hh\:mm\:ss");
        }
        if(ts.TotalHours > 1)
        {
            return ts.ToString(@"hh\:mm\:ss");
        }

        return ts.ToString(@"mm\:ss");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class FloatToPercentageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (float)value * 100;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (double)value / 100;
    }
}

public class BoolToContentConverter : IValueConverter
{
    public required FrameworkElement TrueContent { get; set; }
    public required FrameworkElement FalseContent { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if((bool)value)
        {
            return TrueContent;
        }
        return FalseContent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

// The Bitmap and byte[] payloads these two converters decode come from the framework-neutral
// media view models (WinTabber.ViewModels): SessionListItem.Icon and
// MediaSessionViewModel.Thumbnail. Decoding into a WPF-bindable ImageSource lives here, in
// UI.Common, instead of in the view model, so the view model has no WPF dependency.
public class BitmapToImageSourceConverter : IValueConverter
{
    // Never dispose the input Bitmap. InstalledApplicationRepository still owns it through
    // Replay(1).AutoConnect() and shares one instance across every subscriber for that AUMID. A
    // `using` here would break every other subscriber.
    //
    // This runs on every binding evaluation, not once per view model emission: property change,
    // template re-application, and container realization in an ItemsControl. That is acceptable
    // for a session list of one to five items. If it ever shows up in a profile, memoize inside
    // the converter keyed on the source instance with a ConditionalWeakTable. Do not build that
    // now.
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not System.Drawing.Bitmap bitmap)
        {
            return null;
        }

        nint hBitmap = bitmap.GetHbitmap();
        try
        {
            var image = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions()
            );
            if (!image.IsFrozen && image.CanFreeze)
            {
                image.Freeze();
            }
            return image;
        }
        finally
        {
            Windows.Win32.PInvoke.DeleteObject(new Windows.Win32.Graphics.Gdi.HGDIOBJ(hBitmap));
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ImageBytesToImageSourceConverter : IValueConverter
{
    // See BitmapToImageSourceConverter above for why this decode runs on every binding
    // evaluation rather than once per view model emission, and why that is acceptable here.
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes)
        {
            return null;
        }

        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        if (!image.IsFrozen && image.CanFreeze)
        {
            image.Freeze();
        }
        return image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
