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