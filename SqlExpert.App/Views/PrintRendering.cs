using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using SqlExpert.Core.Printing;

namespace SqlExpert.App.Views;

/// <summary>Draws a Core <see cref="PrintPage"/> (points) with WPF primitives. Arial matches the Helvetica metrics used by the layout.</summary>
public static class PrintRenderer
{
    public const double DipPerPoint = 96.0 / 72.0;
    private static readonly Typeface Regular = new(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Bold = new(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Dictionary<double, Pen> Pens = new();
    private static readonly Dictionary<double, Brush> Fills = new();

    public static Size PageSizeDip(PrintLayout layout) => new(layout.PageWidth * DipPerPoint, layout.PageHeight * DipPerPoint);

    public static void Draw(DrawingContext dc, PrintPage page, double pixelsPerDip)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, page.Width * DipPerPoint, page.Height * DipPerPoint));
        dc.PushTransform(new ScaleTransform(DipPerPoint, DipPerPoint));
        foreach (var e in page.Elements)
        {
            switch (e)
            {
                case PrintFill f:
                    dc.DrawRectangle(Fill(f.Gray), null, new Rect(f.X, f.Y, Math.Max(0, f.Width), Math.Max(0, f.Height)));
                    break;
                case PrintLine l:
                    dc.DrawLine(Pen(l.Thickness), new Point(l.X1, l.Y1), new Point(l.X2, l.Y2));
                    break;
                case PrintText t when t.Text.Length > 0:
                {
                    var ft = new FormattedText(t.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        t.Bold ? Bold : Regular, t.FontSize, Brushes.Black, pixelsPerDip)
                    {
                        MaxLineCount = 1,
                        Trimming = TextTrimming.CharacterEllipsis,
                        MaxTextWidth = Math.Max(1, t.Width + 0.5)
                    };
                    var x = t.Align switch
                    {
                        PrintTextAlign.Right => t.X + t.Width - ft.WidthIncludingTrailingWhitespace,
                        PrintTextAlign.Center => t.X + (t.Width - ft.WidthIncludingTrailingWhitespace) / 2,
                        _ => t.X
                    };
                    var y = t.Y + (t.Height - ft.Height) / 2;
                    dc.DrawText(ft, new Point(x, y));
                    break;
                }
            }
        }
        dc.Pop();
    }

    private static Pen Pen(double thickness)
    {
        lock (Pens)
        {
            if (!Pens.TryGetValue(thickness, out var pen))
            {
                pen = new Pen(Brushes.Black, thickness);
                pen.Freeze();
                Pens[thickness] = pen;
            }
            return pen;
        }
    }

    private static Brush Fill(double gray)
    {
        lock (Fills)
        {
            if (!Fills.TryGetValue(gray, out var brush))
            {
                var g = (byte)Math.Clamp(Math.Round(gray * 255), 0, 255);
                brush = new SolidColorBrush(Color.FromRgb(g, g, g));
                brush.Freeze();
                Fills[gray] = brush;
            }
            return brush;
        }
    }
}

/// <summary>One preview page; draws itself only when the viewer shows it.</summary>
public sealed class PrintPageElement : FrameworkElement
{
    private readonly PrintLayout _layout;
    private readonly int _index;

    public PrintPageElement(PrintLayout layout, int index)
    {
        _layout = layout;
        _index = index;
        var size = PrintRenderer.PageSizeDip(layout);
        Width = size.Width;
        Height = size.Height;
    }

    protected override void OnRender(DrawingContext dc)
    {
        try
        {
            PrintRenderer.Draw(dc, _layout.GetPage(_index), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
        catch (Exception)
        {
            // A drawing failure must never break the viewer.
        }
    }
}

/// <summary>Paginator used for Print and XPS export: every page is drawn into a DrawingVisual on request.</summary>
public sealed class TablePaginator : DocumentPaginator
{
    private readonly PrintLayout _layout;

    public TablePaginator(PrintLayout layout)
    {
        _layout = layout;
        PageSize = PrintRenderer.PageSizeDip(layout);
    }

    public override bool IsPageCountValid => true;
    public override int PageCount => _layout.PageCount;
    public override Size PageSize { get; set; }
    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= _layout.PageCount) return DocumentPage.Missing;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            PrintRenderer.Draw(dc, _layout.GetPage(pageNumber), 1.0);
        var size = PrintRenderer.PageSizeDip(_layout);
        return new DocumentPage(visual, size, new Rect(size), new Rect(size));
    }
}
