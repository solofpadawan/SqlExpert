using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SqlExpert.Core.Services;

namespace SqlExpert.App.Views;

/// <summary>Color Mark ("#RRGGBB") → frozen brush; null/blank/invalid → null (nothing drawn).</summary>
public sealed class ColorMarkBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = ColorMarks.Normalize(value as string);
        if (hex is null) return null;
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

/// <summary>Visible when the Color Mark is a valid color, Collapsed otherwise.</summary>
public sealed class ColorMarkVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ColorMarks.Normalize(value as string) is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
