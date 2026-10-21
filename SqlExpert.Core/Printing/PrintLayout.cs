using System.Globalization;
using SqlExpert.Core.DataForms;

namespace SqlExpert.Core.Printing;

public enum PrintTextAlign { Left, Right, Center }

/// <summary>Something drawn on a printed page. Coordinates in points (1/72"), origin at the top-left corner.</summary>
public abstract record PrintElement;
public sealed record PrintText(double X, double Y, double Width, double Height, string Text, double FontSize, bool Bold, PrintTextAlign Align) : PrintElement;
public sealed record PrintLine(double X1, double Y1, double X2, double Y2, double Thickness) : PrintElement;
/// <summary>Filled rectangle; <see cref="Gray"/> 0 = black … 1 = white.</summary>
public sealed record PrintFill(double X, double Y, double Width, double Height, double Gray) : PrintElement;

public sealed class PrintPage
{
    public int Index { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public int FirstRow { get; init; }
    public int RowCount { get; init; }
    public int Band { get; init; }
    public IReadOnlyList<PrintElement> Elements { get; init; } = Array.Empty<PrintElement>();
}

/// <summary>Already formatted table to print (one string per cell).</summary>
public sealed class PrintTable
{
    public IReadOnlyList<string> Headers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<bool> AlignRight { get; init; } = Array.Empty<bool>();
    public IReadOnlyList<string[]> Rows { get; init; } = Array.Empty<string[]>();
    /// <summary>Left part of the page footer, e.g. "[dbo].[CLIENTES] · 1.899 registro(s) · 20/10/2026 09:22".</summary>
    public string FooterText { get; init; } = "";

    /// <summary>
    /// Formats the fetched rows for printing (see <see cref="DataValueText.FormatForPrint"/>).
    /// Reports progress every 500 rows; honours <paramref name="ct"/>.
    /// </summary>
    public static PrintTable Build(
        IReadOnlyList<string> headers,
        IReadOnlyList<bool> alignRight,
        IReadOnlyList<bool> memoColumns,
        IReadOnlyList<object?[]> rows,
        bool printBlobAndMemo,
        CultureInfo culture,
        string footerText,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var n = headers.Count;
        var result = new List<string[]>(rows.Count);
        for (var r = 0; r < rows.Count; r++)
        {
            if ((r & 511) == 0)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(r);
            }
            var src = rows[r];
            var cells = new string[n];
            for (var c = 0; c < n; c++)
            {
                var v = c < src.Length ? src[c] : null;
                var memo = c < memoColumns.Count && memoColumns[c];
                cells[c] = DataValueText.FormatForPrint(v, memo, printBlobAndMemo, culture);
            }
            result.Add(cells);
        }
        progress?.Report(rows.Count);

        return new PrintTable
        {
            Headers = headers.ToArray(),
            AlignRight = Enumerable.Range(0, n).Select(i => i < alignRight.Count && alignRight[i]).ToArray(),
            Rows = result,
            FooterText = footerText
        };
    }
}

/// <summary>Page geometry and options. Sizes in points; defaults: A4 portrait, 1 cm margins, 8 pt text.</summary>
public sealed class PrintSettings
{
    public const double A4Width = 595.28;
    public const double A4Height = 841.89;

    public double PageWidth { get; init; } = A4Width;
    public double PageHeight { get; init; } = A4Height;
    /// <summary>Swaps width and height.</summary>
    public bool Landscape { get; init; }
    public double Margin { get; init; } = 28.35;
    public double FontSize { get; init; } = 8;
    public double PaddingX { get; init; } = 3;
    public double PaddingY { get; init; } = 1.5;
    /// <summary>Printed on top of every page (empty = no title line).</summary>
    public string Title { get; init; } = "";
    /// <summary>No column wider than this fraction of the printable width.</summary>
    public double MaxColumnFraction { get; init; } = 0.45;
    /// <summary>Rows inspected to size the columns (the rest is trimmed with "…").</summary>
    public int MeasureSampleRows { get; init; } = 2000;

    public double EffectiveWidth => Landscape ? Math.Max(PageWidth, PageHeight) : Math.Min(PageWidth, PageHeight);
    public double EffectiveHeight => Landscape ? Math.Min(PageWidth, PageHeight) : Math.Max(PageWidth, PageHeight);
}

/// <summary>
/// Paginates a <see cref="PrintTable"/>: columns sized to their content (shrunk to fit the page, or split into bands of
/// columns when they cannot fit), header row repeated on every page, title on top, footer with page numbers.
/// Pages are produced on demand by <see cref="GetPage"/>.
/// </summary>
public sealed class PrintLayout
{
    private readonly PrintTable _table;
    private readonly double[] _widths;
    private readonly (int First, int Count)[] _bands;

    private PrintLayout(PrintTable table, PrintSettings settings, double[] widths, (int, int)[] bands)
    {
        _table = table;
        Settings = settings;
        _widths = widths;
        _bands = bands;

        PageWidth = settings.EffectiveWidth;
        PageHeight = settings.EffectiveHeight;
        RowHeight = Math.Round(settings.FontSize * 1.2 + 2 * settings.PaddingY, 2);
        TitleHeight = string.IsNullOrWhiteSpace(settings.Title) ? 0 : Math.Round(settings.FontSize * 1.6 * 1.25 + 6, 2);
        FooterHeight = Math.Round(settings.FontSize * 1.25 + 6, 2);
        var available = PageHeight - 2 * settings.Margin - TitleHeight - FooterHeight - RowHeight;
        RowsPerPage = Math.Max(1, (int)Math.Floor(available / RowHeight + 1e-9));
        RowChunks = Math.Max(1, (int)Math.Ceiling(table.Rows.Count / (double)RowsPerPage));
        PageCount = RowChunks * _bands.Length;
    }

    public PrintSettings Settings { get; }
    public PrintTable Table => _table;
    public double PageWidth { get; }
    public double PageHeight { get; }
    public double RowHeight { get; }
    public double TitleHeight { get; }
    public double FooterHeight { get; }
    public int RowsPerPage { get; }
    public int RowChunks { get; }
    public int PageCount { get; }
    public IReadOnlyList<double> ColumnWidths => _widths;
    public IReadOnlyList<(int First, int Count)> Bands => _bands;
    public double PrintableWidth => PageWidth - 2 * Settings.Margin;

    public static PrintLayout Create(PrintTable table, PrintSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(settings);
        var fs = settings.FontSize;
        var pad = 2 * settings.PaddingX;
        var printable = Math.Max(20, settings.EffectiveWidth - 2 * settings.Margin);
        var maxCol = Math.Max(20, printable * settings.MaxColumnFraction);
        var n = table.Headers.Count;

        var natural = new double[n];
        var minimum = new double[n];
        var sample = Math.Min(table.Rows.Count, Math.Max(0, settings.MeasureSampleRows));
        for (var c = 0; c < n; c++)
        {
            var header = TextMetrics.Measure(table.Headers[c], fs, bold: true);
            var w = header;
            for (var r = 0; r < sample; r++)
            {
                if ((r & 1023) == 0) ct.ThrowIfCancellationRequested();
                var cells = table.Rows[r];
                if (c < cells.Length)
                    w = Math.Max(w, TextMetrics.Measure(cells[c], fs));
            }

            natural[c] = Math.Min(maxCol, Math.Max(w, TextMetrics.Measure("00", fs))) + pad + 1; // +1: rounding slack
            var min = Math.Max(TextMetrics.Measure("000000", fs), Math.Min(header, TextMetrics.Measure("00000000000000", fs))) + pad;
            minimum[c] = Math.Min(natural[c], Math.Min(min, printable));
        }

        // Bands of columns: greedy on the minimum widths.
        var bands = new List<(int First, int Count)>();
        var start = 0;
        double sum = 0;
        for (var c = 0; c < n; c++)
        {
            if (c > start && sum + minimum[c] > printable + 1e-6)
            {
                bands.Add((start, c - start));
                start = c;
                sum = 0;
            }
            sum += minimum[c];
        }
        bands.Add((start, n - start));

        var widths = new double[n];
        foreach (var (first, count) in bands)
            FitBand(natural, minimum, widths, first, count, printable);

        return new PrintLayout(table, settings, widths, bands.ToArray());
    }

    /// <summary>Natural widths when they fit; else shrink every column between its minimum and natural width.</summary>
    private static void FitBand(double[] natural, double[] minimum, double[] widths, int first, int count, double printable)
    {
        double sumNat = 0, sumMin = 0;
        for (var c = first; c < first + count; c++) { sumNat += natural[c]; sumMin += minimum[c]; }
        double t;
        if (sumNat <= printable || sumNat - sumMin < 1e-9) t = 1;
        else t = Math.Max(0, (printable - sumMin) / (sumNat - sumMin));
        for (var c = first; c < first + count; c++)
            widths[c] = Math.Min(printable, Math.Floor((minimum[c] + (natural[c] - minimum[c]) * t) * 100) / 100);
    }

    public string PageLabel(int index)
    {
        var label = $"Página {(index + 1).ToString(CultureInfo.InvariantCulture)} de {PageCount.ToString(CultureInfo.InvariantCulture)}";
        if (_bands.Length <= 1) return label;
        var band = _bands[index % _bands.Length];
        return label + $" (colunas {band.First + 1}–{band.First + band.Count})";
    }

    /// <summary>Builds the drawing of page <paramref name="index"/> (0-based): rows chunk-major, then column band.</summary>
    public PrintPage GetPage(int index)
    {
        if (index < 0 || index >= PageCount) throw new ArgumentOutOfRangeException(nameof(index));
        var s = Settings;
        var fs = s.FontSize;
        var chunk = index / _bands.Length;
        var bandIndex = index % _bands.Length;
        var (firstCol, colCount) = _bands[bandIndex];
        var firstRow = chunk * RowsPerPage;
        var rowCount = Math.Max(0, Math.Min(RowsPerPage, _table.Rows.Count - firstRow));

        var els = new List<PrintElement>(8 + (rowCount + 1) * (colCount + 1) + colCount);
        var x0 = s.Margin;
        var y = s.Margin;

        if (TitleHeight > 0)
        {
            var titleSize = Math.Round(fs * 1.6, 2);
            els.Add(new PrintText(x0, y, PrintableWidth, TitleHeight - 6, TextMetrics.Fit(s.Title.Trim(), PrintableWidth, titleSize, true),
                titleSize, true, PrintTextAlign.Left));
            y += TitleHeight;
        }

        double tableWidth = 0;
        for (var c = firstCol; c < firstCol + colCount; c++) tableWidth += _widths[c];
        var tableTop = y;
        var tableBottom = tableTop + RowHeight * (rowCount + 1);

        // Header background.
        els.Add(new PrintFill(x0, tableTop, tableWidth, RowHeight, 0.88));

        // Horizontal lines (top, under header, every row).
        for (var r = 0; r <= rowCount + 1; r++)
        {
            var ly = tableTop + r * RowHeight;
            els.Add(new PrintLine(x0, ly, x0 + tableWidth, ly, r <= 1 || r == rowCount + 1 ? 0.75 : 0.4));
        }

        // Vertical lines.
        var lx = x0;
        els.Add(new PrintLine(lx, tableTop, lx, tableBottom, 0.75));
        for (var c = firstCol; c < firstCol + colCount; c++)
        {
            lx += _widths[c];
            els.Add(new PrintLine(lx, tableTop, lx, tableBottom, c == firstCol + colCount - 1 ? 0.75 : 0.4));
        }

        // Header + cells.
        var cx = x0;
        for (var c = firstCol; c < firstCol + colCount; c++)
        {
            var w = _widths[c];
            var inner = w - 2 * s.PaddingX;
            var right = c < _table.AlignRight.Count && _table.AlignRight[c];
            els.Add(new PrintText(cx + s.PaddingX, tableTop + s.PaddingY, inner, RowHeight - 2 * s.PaddingY,
                TextMetrics.Fit(_table.Headers[c], inner, fs, true), fs, true, PrintTextAlign.Left));
            for (var r = 0; r < rowCount; r++)
            {
                var cells = _table.Rows[firstRow + r];
                var text = c < cells.Length ? cells[c] : "";
                if (text.Length == 0) continue;
                els.Add(new PrintText(cx + s.PaddingX, tableTop + (r + 1) * RowHeight + s.PaddingY, inner, RowHeight - 2 * s.PaddingY,
                    TextMetrics.Fit(text, inner, fs), fs, false, right ? PrintTextAlign.Right : PrintTextAlign.Left));
            }
            cx += w;
        }

        // Footer.
        var footerY = PageHeight - s.Margin - FooterHeight + 4;
        var footSize = Math.Max(5, fs - 1);
        var label = PageLabel(index);
        var labelWidth = TextMetrics.Measure(label, footSize) + 2;
        els.Add(new PrintLine(x0, footerY - 2, x0 + PrintableWidth, footerY - 2, 0.4));
        if (_table.FooterText.Length > 0)
            els.Add(new PrintText(x0, footerY, PrintableWidth - labelWidth - 8, FooterHeight - 4,
                TextMetrics.Fit(_table.FooterText, PrintableWidth - labelWidth - 8, footSize), footSize, false, PrintTextAlign.Left));
        els.Add(new PrintText(x0 + PrintableWidth - labelWidth, footerY, labelWidth, FooterHeight - 4, label, footSize, false, PrintTextAlign.Right));

        return new PrintPage
        {
            Index = index,
            Width = PageWidth,
            Height = PageHeight,
            FirstRow = firstRow,
            RowCount = rowCount,
            Band = bandIndex,
            Elements = els
        };
    }
}
