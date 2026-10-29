using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SqlExpert.App.ViewModels;

namespace SqlExpert.App.Views;

/// <summary>
/// Small flat button next to a document title: copies the document's bare object name
/// (<see cref="DocumentViewModel.CopyableName"/>) to the clipboard and shows a check mark for a moment.
/// </summary>
public sealed class CopyNameButton : Button
{
    private static readonly ImageSource CheckIcon = BuildCheck();
    private readonly IconImage _icon = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.2) };

    public CopyNameButton()
    {
        Focusable = false;
        Cursor = Cursors.Hand;
        Padding = new Thickness(2);
        VerticalAlignment = VerticalAlignment.Center;
        Template = BuildTemplate();
        _icon.SetResourceReference(IconImage.IconProperty, "Icon.Copy");
        _icon.Width = _icon.Height = 14;
        Content = _icon;
        _timer.Tick += (_, _) => Reset();
        DataContextChanged += (_, _) => Reset();
        ToolTipService.SetInitialShowDelay(this, 300);
    }

    private DocumentViewModel? Document => DataContext as DocumentViewModel;

    private void Reset()
    {
        _timer.Stop();
        _icon.SetResourceReference(IconImage.IconProperty, "Icon.Copy");
        ToolTip = Document?.CopyNameToolTip ?? "Copiar nome";
    }

    protected override void OnClick()
    {
        base.OnClick();
        var name = Document?.CopyableName;
        if (string.IsNullOrEmpty(name)) return;
        try { Clipboard.SetText(name); }
        catch (System.Runtime.InteropServices.COMException) { return; } // clipboard busy: leave the icon as is
        _icon.Icon = CheckIcon;
        ToolTip = "Copiado!";
        _timer.Stop();
        _timer.Start();
    }

    private static ControlTemplate BuildTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var t = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), "Bd"));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x9F, 0xB4, 0xCC)), "Bd"));
        t.Triggers.Add(hover);
        var press = new Trigger { Property = IsPressedProperty, Value = true };
        press.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xDC, 0xEB, 0xFA)), "Bd"));
        t.Triggers.Add(press);
        return t;
    }

    /// <summary>Check mark in the app's muted blue (no green).</summary>
    private static ImageSource BuildCheck()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x3E, 0x6E, 0xA8)), 1.8)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var g = new DrawingGroup { ClipGeometry = Geometry.Parse("M0,0 H16 V16 H0 Z") };
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, Geometry.Parse("M0,0 H16 V16 H0 Z")));
        g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M3,8.5 L6.5,12 L13,4.5")));
        var img = new DrawingImage(g);
        img.Freeze();
        return img;
    }
}
