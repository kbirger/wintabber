using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;

namespace WinTabber.UI.Common.ValueConverters;

public class BoolToThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (bool)value ? new Thickness(1) : new Thickness(0);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class BoolToBrushConverter : IValueConverter
{
    public Brush EditBrush { get; set; } = new SolidColorBrush(Colors.White);
    public Brush ViewBrush { get; set; } = new SolidColorBrush(Colors.Transparent);

    public object Convert(object value, Type targetType, object parameter, string language)
        => (bool)value ? EditBrush : ViewBrush;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => !(bool)value;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => !(bool)value;
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (bool)value ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => (Visibility)value == Visibility.Visible;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (bool)value ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => (Visibility)value != Visibility.Visible;
}

public class WindowStateToVisibilityConverter : IValueConverter
{
    // NOTE: Microsoft.UI.Xaml.WindowState does not exist in the installed Microsoft.WindowsAppSDK
    // package (confirmed via compiler error CS0246). WinUI 3's windowing-state enum lives on the
    // AppWindow's OverlappedPresenter instead, as Microsoft.UI.Windowing.OverlappedPresenterState
    // (Maximized/Minimized/Restored — there is no "Normal" member, so Restored is the substitute).
    public OverlappedPresenterState TargetState { get; set; } = OverlappedPresenterState.Restored;

    public object Convert(object value, Type targetType, object parameter, string language)
        => (OverlappedPresenterState)value == TargetState ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => (Visibility)value == Visibility.Visible ? TargetState : OverlappedPresenterState.Restored;
}

public class EmptyCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (int)value == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value == null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class EnumToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value?.ToString() ?? (object)0;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => Enum.Parse(targetType, (string)value);
}

public class StringToEnumConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => Enum.Parse(targetType, (string)value);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class EnumValuesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => Enum.GetNames(value.GetType());

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class TimeSpanToFloatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => ((TimeSpan)value).TotalSeconds;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => TimeSpan.FromSeconds((double)value);
}

public class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var ts = (TimeSpan)value;
        if (ts.TotalHours > 1)
        {
            return ts.ToString(@"dd\:hh\:mm\:ss");
        }
        if (ts.TotalHours > 1)
        {
            return ts.ToString(@"hh\:mm\:ss");
        }

        return ts.ToString(@"mm\:ss");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class FloatToPercentageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (float)value * 100;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => (double)value / 100;
}

/// <summary>Bool -> tile opacity, for the WindowSelectorWindow tile dimming treatment (WinUI3
/// port of a WPF Style.Triggers pair -- see WindowItem.IsDimmed's doc comment for why the two
/// source flags it used to read are collapsed into one bool before reaching this converter).</summary>
public class DimIfTrueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (bool)value ? 0.4 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class BoolToContentConverter : IValueConverter
{
    public required FrameworkElement TrueContent { get; set; }
    public required FrameworkElement FalseContent { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
        => (bool)value ? TrueContent : FalseContent;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

// The Bitmap and byte[] payloads these two converters decode come from the framework-neutral
// media view models (WinTabber.ViewModels): SessionListItem.Icon and
// MediaSessionViewModel.Thumbnail. Decoding into a WinUI 3-bindable BitmapImage lives here, in
// UI.Common, instead of in the view model, so the view model has no WinUI dependency.
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
    //
    // BitmapSource.SetSource (inherited by BitmapImage) is confirmed synchronous in the pinned
    // Windows App SDK 1.8.251105000 metadata (Microsoft.UI.Xaml.winmd): it returns void, not
    // IAsyncAction, unlike the SetSourceAsync overload the async-decode WinUI view models used
    // to call directly. That lets this converter, like its WPF counterpart, decode and return
    // synchronously from Convert.
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not System.Drawing.Bitmap bitmap)
        {
            return null;
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.SetSource(stream.AsRandomAccessStream());
        return image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class ImageBytesToImageSourceConverter : IValueConverter
{
    // See BitmapToImageSourceConverter above for why this decode runs on every binding
    // evaluation rather than once per view model emission, and why that is acceptable here.
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not byte[] { Length: > 0 } bytes)
        {
            return null;
        }

        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.SetSource(stream.AsRandomAccessStream());
        return image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
