using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SqlExpert.App.Views;

/// <summary>
/// 16x16 vector icon (an "Icon.*" DrawingImage of Themes/Icons.xaml). When disabled - directly or because its
/// button / menu item is - it shows a grey copy of the drawing at reduced opacity, so unavailable commands look
/// greyed out like the rest of the classic UI.
/// </summary>
public sealed class IconImage : Image
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(ImageSource), typeof(IconImage), new PropertyMetadata(null, (d, _) => ((IconImage)d).UpdateSource()));

    private static readonly ConditionalWeakTable<ImageSource, ImageSource> GrayCache = new();

    public IconImage()
    {
        Width = 16;
        Height = 16;
        Stretch = Stretch.Uniform;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Focusable = false;
        IsEnabledChanged += (_, _) => UpdateSource();
    }

    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    private void UpdateSource()
    {
        var icon = Icon;
        Source = icon is null || IsEnabled ? icon : GrayCache.GetValue(icon, Gray);
        Opacity = IsEnabled ? 1.0 : 0.45;
    }

    /// <summary>Grey copy of a DrawingImage (every brush → luminance grey, lightened). Other sources stay as they are.</summary>
    internal static ImageSource Gray(ImageSource source)
    {
        if (source is not DrawingImage { Drawing: { } drawing }) return source;
        var copy = drawing.CloneCurrentValue();
        Walk(copy);
        var image = new DrawingImage(copy);
        image.Freeze();
        return image;
    }

    private static void Walk(Drawing drawing)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                foreach (var child in group.Children) Walk(child);
                break;
            case GeometryDrawing g:
                g.Brush = GrayBrush(g.Brush);
                if (g.Pen is { } pen) pen.Brush = GrayBrush(pen.Brush);
                break;
        }
    }

    private static Brush? GrayBrush(Brush? brush)
    {
        if (brush is not SolidColorBrush s) return brush;
        var c = s.Color;
        var lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        var v = (byte)Math.Round(lum + (255 - lum) * 0.35);
        var g = new SolidColorBrush(Color.FromArgb(c.A, v, v, v));
        g.Freeze();
        return g;
    }
}

/// <summary>"Icon.Table" → the DrawingImage resource (null key or unknown key → null: no icon).</summary>
public sealed class IconKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && key.Length > 0 ? Application.Current?.TryFindResource(key) as ImageSource : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
