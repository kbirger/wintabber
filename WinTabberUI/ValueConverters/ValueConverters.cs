using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace WinTabberUI.ValueConverters;

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
