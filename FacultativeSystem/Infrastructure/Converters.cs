using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FacultativeSystem.Infrastructure;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNotNull = value is not null && (value is not string str || !string.IsNullOrWhiteSpace(str));
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            isNotNull = !isNotNull;
        return isNotNull ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly Brush ActiveBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCFCE7"));
    private static readonly Brush InactiveBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE2E2"));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToStatusForegroundConverter : IValueConverter
{
    private static readonly Brush ActiveBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#166534"));
    private static readonly Brush InactiveBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#991B1B"));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
