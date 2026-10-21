using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace SqlExpert.Core.Printing;

/// <summary>
/// Minimal PDF 1.4 writer for a <see cref="PrintLayout"/>: base-14 Helvetica / Helvetica-Bold (WinAnsi), lines,
/// gray fills, Flate-compressed page streams. Characters outside Windows-1252 print as "?".
/// </summary>
public static class PdfTableWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Lazy<Encoding> WinAnsi = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);
    });

    /// <summary>Writes every page. <paramref name="progress"/> receives the number of pages written.</summary>
    public static void Write(Stream output, PrintLayout layout, string documentTitle, DateTime createdAt,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(layout);
        var pdf = new PdfOut(output);
        var pageCount = layout.PageCount;
        // Object numbers: 1 catalog, 2 pages, 3 F1, 4 F2, 5 info, then (page, content) pairs.
        var objCount = 5 + 2 * pageCount;
        var offsets = new long[objCount + 1];

        pdf.Raw("%PDF-1.4\n%\u00E2\u00E3\u00CF\u00D3\n");

        offsets[1] = pdf.Position;
        pdf.Raw("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[3] = pdf.Position;
        pdf.Raw("3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");
        offsets[4] = pdf.Position;
        pdf.Raw("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");
        offsets[5] = pdf.Position;
        pdf.Raw("5 0 obj\n<< /Title " + TextString(documentTitle) + " /Producer (SqlExpert) /Creator (SqlExpert Print Data) /CreationDate (D:"
                + createdAt.ToString("yyyyMMddHHmmss", Inv) + PdfOffset(createdAt) + ") >>\nendobj\n");

        for (var i = 0; i < pageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var page = layout.GetPage(i);
            var pageObj = 6 + 2 * i;
            var contentObj = pageObj + 1;
            offsets[pageObj] = pdf.Position;
            pdf.Raw($"{pageObj} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(page.Width)} {N(page.Height)}] " +
                    $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObj} 0 R >>\nendobj\n");

            var content = Compress(PageContent(page));
            offsets[contentObj] = pdf.Position;
            pdf.Raw($"{contentObj} 0 obj\n<< /Length {content.Length.ToString(Inv)} /Filter /FlateDecode >>\nstream\n");
            pdf.Bytes(content);
            pdf.Raw("\nendstream\nendobj\n");
            if ((i & 7) == 7 || i == pageCount - 1) progress?.Report(i + 1);
        }

        offsets[2] = pdf.Position;
        var kids = new StringBuilder();
        for (var i = 0; i < pageCount; i++)
        {
            if (i > 0) kids.Append(' ');
            kids.Append((6 + 2 * i).ToString(Inv)).Append(" 0 R");
        }
        pdf.Raw($"2 0 obj\n<< /Type /Pages /Count {pageCount.ToString(Inv)} /Kids [{kids}] >>\nendobj\n");

        var xref = pdf.Position;
        var sb = new StringBuilder();
        sb.Append("xref\n0 ").Append((objCount + 1).ToString(Inv)).Append('\n');
        sb.Append("0000000000 65535 f \n");
        for (var o = 1; o <= objCount; o++)
            sb.Append(offsets[o].ToString("D10", Inv)).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size ").Append((objCount + 1).ToString(Inv)).Append(" /Root 1 0 R /Info 5 0 R >>\nstartxref\n")
          .Append(xref.ToString(Inv)).Append("\n%%EOF\n");
        pdf.Raw(sb.ToString());
        output.Flush();
    }

    /// <summary>Uncompressed content stream of one page (public for tests).</summary>
    public static string PageContent(PrintPage page)
    {
        var sb = new StringBuilder(4096);
        var h = page.Height;
        foreach (var e in page.Elements)
        {
            switch (e)
            {
                case PrintFill f:
                    sb.Append(N(f.Gray)).Append(" g ").Append(N(f.X)).Append(' ').Append(N(h - f.Y - f.Height)).Append(' ')
                      .Append(N(f.Width)).Append(' ').Append(N(f.Height)).Append(" re f 0 g\n");
                    break;
                case PrintLine l:
                    sb.Append(N(l.Thickness)).Append(" w ").Append(N(l.X1)).Append(' ').Append(N(h - l.Y1)).Append(" m ")
                      .Append(N(l.X2)).Append(' ').Append(N(h - l.Y2)).Append(" l S\n");
                    break;
                case PrintText t when t.Text.Length > 0:
                {
                    var width = TextMetrics.Measure(t.Text, t.FontSize, t.Bold);
                    var x = t.Align switch
                    {
                        PrintTextAlign.Right => t.X + t.Width - width,
                        PrintTextAlign.Center => t.X + (t.Width - width) / 2,
                        _ => t.X
                    };
                    // Baseline: centred line box, Helvetica ascent ≈ 0.77 em above the baseline.
                    var lineTop = t.Y + Math.Max(0, (t.Height - t.FontSize * 1.15) / 2);
                    var baseline = lineTop + t.FontSize * 0.9;
                    sb.Append("BT /").Append(t.Bold ? "F2 " : "F1 ").Append(N(t.FontSize)).Append(" Tf ")
                      .Append(N(x)).Append(' ').Append(N(h - baseline)).Append(" Td ").Append(LiteralString(t.Text)).Append(" Tj ET\n");
                    break;
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>PDF literal string in WinAnsi: "(a\(b\))", non-ASCII bytes as octal escapes.</summary>
    public static string LiteralString(string text)
    {
        var bytes = WinAnsi.Value.GetBytes(text);
        var sb = new StringBuilder(bytes.Length + 2);
        sb.Append('(');
        foreach (var b in bytes)
        {
            switch (b)
            {
                case (byte)'(': sb.Append("\\("); break;
                case (byte)')': sb.Append("\\)"); break;
                case (byte)'\\': sb.Append("\\\\"); break;
                default:
                    if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                    else sb.Append((char)b);
                    break;
            }
        }
        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>Document-info text string: ASCII literal, or UTF-16BE hex with BOM.</summary>
    public static string TextString(string text)
    {
        text ??= "";
        if (text.All(ch => ch >= 32 && ch <= 126)) return LiteralString(text);
        var sb = new StringBuilder("<FEFF");
        foreach (var b in Encoding.BigEndianUnicode.GetBytes(text)) sb.Append(b.ToString("X2", Inv));
        return sb.Append('>').ToString();
    }

    /// <summary>"-03'00'" style offset of the local time zone ("Z" for UTC values).</summary>
    private static string PdfOffset(DateTime t)
    {
        if (t.Kind == DateTimeKind.Utc) return "Z";
        var off = TimeZoneInfo.Local.GetUtcOffset(t);
        var sign = off < TimeSpan.Zero ? "-" : "+";
        off = off.Duration();
        return $"{sign}{off.Hours.ToString("00", Inv)}'{off.Minutes.ToString("00", Inv)}'";
    }

    private static string N(double v)
    {
        var r = Math.Round(v, 2);
        if (Math.Abs(r) < 0.005) r = 0;
        return r.ToString("0.##", Inv);
    }

    private static byte[] Compress(string content)
    {
        var raw = WinAnsi.Value.GetBytes(content);
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw, 0, raw.Length);
        return ms.ToArray();
    }

    /// <summary>Byte-counting writer (offsets for the xref table).</summary>
    private sealed class PdfOut
    {
        private readonly Stream _s;
        private long _written;
        public PdfOut(Stream s) => _s = s;
        public long Position => _written;

        public void Raw(string text)
        {
            // Header/dictionary text is ASCII except the binary comment (Latin-1 bytes).
            var bytes = Encoding.Latin1.GetBytes(text);
            Bytes(bytes);
        }

        public void Bytes(byte[] bytes)
        {
            _s.Write(bytes, 0, bytes.Length);
            _written += bytes.Length;
        }
    }
}
