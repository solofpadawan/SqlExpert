using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using SqlExpert.Core.DataForms;
using SqlExpert.Core.Models;
using SqlExpert.Core.Printing;

namespace SqlExpert.Tests;

public class DataValueTextTests
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    [Fact]
    public void Format_MatchesGridRules()
    {
        Assert.Equal("", DataValueText.Format(DBNull.Value, Br));
        Assert.Equal("<null>", DataValueText.FormatCell(DBNull.Value, Br));
        Assert.Equal("<null>", DataValueText.FormatCell(null, Br));
        Assert.Equal("20/10/2026", DataValueText.Format(new DateTime(2026, 10, 20), Br));
        Assert.Equal("20/10/2026 09:22:00", DataValueText.Format(new DateTime(2026, 10, 20, 9, 22, 0), Br));
        Assert.Equal("20/10/2026 09:22:00.5", DataValueText.Format(new DateTime(2026, 10, 20, 9, 22, 0, 500), Br));
        Assert.Equal("1234,5", DataValueText.Format(1234.5m, Br));
        Assert.Equal("0x0102FF", DataValueText.Format(new byte[] { 1, 2, 255 }, Br));
    }

    [Fact]
    public void ToHex_TruncatesWithDots()
    {
        Assert.Equal("0x", DataValueText.ToHex(Array.Empty<byte>()));
        Assert.Equal("0x0001...", DataValueText.ToHex(new byte[] { 0, 1, 2 }, 2));
    }

    [Fact]
    public void BinarySummary_ShowsSizeAndPrefix()
    {
        var s = DataValueText.BinarySummary(new byte[2000], Br);
        Assert.StartsWith("<binary 2.000 bytes> 0x0000", s);
        Assert.EndsWith("...", s);
        Assert.Equal("", DataValueText.BinarySummary(DBNull.Value, Br));
    }

    [Fact]
    public void Parse_GridAndFormRules()
    {
        Assert.Equal(DBNull.Value, DataValueText.Parse("<null>", typeof(string), Br));
        Assert.Equal("<null>", DataValueText.Parse("<null>", typeof(string), Br, nullMarkerAllowed: false));
        Assert.Equal("", DataValueText.Parse("", typeof(string), Br));
        Assert.Equal(DBNull.Value, DataValueText.Parse("  ", typeof(int), Br));
        Assert.Equal(42, DataValueText.Parse(" 42 ", typeof(int), Br));
        Assert.Equal(1234.56m, DataValueText.Parse("1.234,56", typeof(decimal), Br));
        Assert.Equal(true, DataValueText.Parse("1", typeof(bool), Br));
        Assert.Equal(new DateTime(2020, 7, 12, 22, 4, 0), DataValueText.Parse("12/07/2020 22:04", typeof(DateTime), Br));
        Assert.Throws<FormatException>(() => DataValueText.Parse("abc", typeof(int), Br));
    }

    [Fact]
    public void FormatForPrint_MemoAndBlobPlaceholders()
    {
        var longText = new string('x', 300);
        Assert.Equal("", DataValueText.FormatForPrint(DBNull.Value, false, false, Br));
        Assert.Equal("(BLOB)", DataValueText.FormatForPrint(new byte[] { 1 }, false, false, Br));
        Assert.Equal("0x01", DataValueText.FormatForPrint(new byte[] { 1 }, false, true, Br));
        Assert.Equal("(MEMO)", DataValueText.FormatForPrint("curto", true, false, Br));
        Assert.Equal("(MEMO)", DataValueText.FormatForPrint(longText, false, false, Br));
        Assert.Equal(longText, DataValueText.FormatForPrint(longText, true, true, Br));
        Assert.Equal("curto", DataValueText.FormatForPrint("curto", false, false, Br));
        Assert.Equal("a b c", DataValueText.FormatForPrint("a\r\n\r\nb\tc", false, true, Br));
        Assert.Equal(DataValueText.MaxPrintChars, DataValueText.FormatForPrint(new string('y', 5000), true, true, Br).Length);
        Assert.Equal("12,5", DataValueText.FormatForPrint(12.5m, false, false, Br));
    }
}

public class FormFieldLayoutTests
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static DataColumnInfo Col(string type, int maxLength = 0, int precision = 0, int scale = 0, bool nullable = true,
        bool pk = false, bool identity = false, string? baseType = null, bool clr = false) => new()
    {
        Name = "COL",
        TypeName = type,
        BaseTypeName = baseType ?? type,
        MaxLength = maxLength,
        Precision = precision,
        Scale = scale,
        IsNullable = nullable,
        IsPrimaryKey = pk,
        KeyOrdinal = pk ? 1 : 0,
        IsIdentity = identity,
        IsClrType = clr
    };

    [Theory]
    [InlineData("int", 4, 10, 0, "INT")]
    [InlineData("nvarchar", 120, 0, 0, "NVARCHAR(60)")]
    [InlineData("nvarchar", -1, 0, 0, "NVARCHAR(MAX)")]
    [InlineData("varchar", 255, 0, 0, "VARCHAR(255)")]
    [InlineData("varbinary", -1, 0, 0, "VARBINARY(MAX)")]
    [InlineData("numeric", 9, 15, 2, "NUMERIC(15,2)")]
    [InlineData("datetime2", 7, 27, 3, "DATETIME2(3)")]
    [InlineData("datetime2", 8, 27, 7, "DATETIME2")]
    [InlineData("datetime", 8, 23, 3, "DATETIME")]
    public void TypeText_Formats(string type, int len, int precision, int scale, string expected)
        => Assert.Equal(expected, FormFieldLayout.TypeText(Col(type, len, precision, scale)));

    [Fact]
    public void TypeText_AliasTypeShowsBase()
        => Assert.Equal("D_NOME (NVARCHAR(60))", FormFieldLayout.TypeText(Col("d_nome", 120, baseType: "nvarchar")));

    [Fact]
    public void MemoColumns()
    {
        Assert.True(FormFieldLayout.IsMemoColumn(Col("nvarchar", -1)));
        Assert.True(FormFieldLayout.IsMemoColumn(Col("varchar", 4000)));
        Assert.True(FormFieldLayout.IsMemoColumn(Col("ntext", 16)));
        Assert.True(FormFieldLayout.IsMemoColumn(Col("xml", -1)));
        Assert.False(FormFieldLayout.IsMemoColumn(Col("nvarchar", 510)));   // 255 chars
        Assert.True(FormFieldLayout.IsMemoColumn(Col("nvarchar", 512)));    // 256 chars
        Assert.False(FormFieldLayout.IsMemoColumn(Col("int", 4)));
        Assert.False(FormFieldLayout.IsMemoColumn(Col("varbinary", -1)));
    }

    [Fact]
    public void KindOf_PicksEditor()
    {
        Assert.Equal(FormFieldKind.Text, FormFieldLayout.KindOf(Col("varchar", 50), typeof(string)));
        Assert.Equal(FormFieldKind.Memo, FormFieldLayout.KindOf(Col("nvarchar", -1), typeof(string)));
        Assert.Equal(FormFieldKind.Number, FormFieldLayout.KindOf(Col("int"), typeof(int)));
        Assert.Equal(FormFieldKind.Number, FormFieldLayout.KindOf(Col("numeric"), typeof(decimal)));
        Assert.Equal(FormFieldKind.Date, FormFieldLayout.KindOf(Col("date"), typeof(DateTime)));
        Assert.Equal(FormFieldKind.DateTime, FormFieldLayout.KindOf(Col("datetime"), typeof(DateTime)));
        Assert.Equal(FormFieldKind.DateTime, FormFieldLayout.KindOf(Col("datetimeoffset"), typeof(DateTimeOffset)));
        Assert.Equal(FormFieldKind.Time, FormFieldLayout.KindOf(Col("time"), typeof(TimeSpan)));
        Assert.Equal(FormFieldKind.Boolean, FormFieldLayout.KindOf(Col("bit"), typeof(bool)));
        Assert.Equal(FormFieldKind.Binary, FormFieldLayout.KindOf(Col("varbinary", -1), typeof(byte[])));
        Assert.Equal(FormFieldKind.ReadOnlyText, FormFieldLayout.KindOf(Col("geography", clr: true), typeof(string)));
        Assert.Equal(FormFieldKind.ReadOnlyText, FormFieldLayout.KindOf(Col("sql_variant"), typeof(object)));
        Assert.Equal(FormFieldKind.Text, FormFieldLayout.KindOf(Col("uniqueidentifier"), typeof(Guid)));
    }

    [Fact]
    public void Build_AppliesGridEditingRules()
    {
        var pk = FormFieldLayout.Build("c0", Col("int", pk: true, nullable: false, identity: true), typeof(int), columnReadOnly: true, tableReadOnly: false);
        Assert.True(pk.IsReadOnly);
        Assert.True(pk.IsBold);
        Assert.False(pk.CanToggleNull);
        Assert.Contains("identity", pk.ToolTip);

        var name = FormFieldLayout.Build("c1", Col("nvarchar", 120), typeof(string), false, false);
        Assert.False(name.IsReadOnly);
        Assert.False(name.IsBold);
        Assert.True(name.CanToggleNull);

        var notNull = FormFieldLayout.Build("c2", Col("int", nullable: false), typeof(int), false, false);
        Assert.True(notNull.IsBold);
        Assert.False(notNull.CanToggleNull);

        var noPk = FormFieldLayout.Build("c1", Col("nvarchar", 120), typeof(string), false, tableReadOnly: true);
        Assert.True(noPk.IsReadOnly);
        Assert.False(noPk.CanToggleNull);

        var bin = FormFieldLayout.Build("c3", Col("varbinary", 16), typeof(byte[]), false, false);
        Assert.True(bin.IsReadOnly);
    }

    [Fact]
    public void DefaultValues()
    {
        Assert.Equal("", FormFieldLayout.DefaultValueFor(typeof(string)));
        Assert.Equal(0, FormFieldLayout.DefaultValueFor(typeof(int)));
        Assert.Equal(0m, FormFieldLayout.DefaultValueFor(typeof(decimal)));
        Assert.Equal((short)0, FormFieldLayout.DefaultValueFor(typeof(short)));
        Assert.Equal(false, FormFieldLayout.DefaultValueFor(typeof(bool)));
        Assert.Equal(DateTime.Today, FormFieldLayout.DefaultValueFor(typeof(DateTime)));
        Assert.Equal(DBNull.Value, FormFieldLayout.DefaultValueFor(typeof(byte[])));
    }

    [Fact]
    public void ReplaceDate_KeepsTimeAndOffset()
    {
        Assert.Equal(new DateTime(2026, 1, 5, 22, 4, 9), FormFieldLayout.ReplaceDate(new DateTime(2020, 7, 12, 22, 4, 9), new DateTime(2026, 1, 5), typeof(DateTime)));
        Assert.Equal(new DateTime(2026, 1, 5), FormFieldLayout.ReplaceDate(DBNull.Value, new DateTime(2026, 1, 5, 13, 0, 0), typeof(DateTime)));
        var o = (DateTimeOffset)FormFieldLayout.ReplaceDate(new DateTimeOffset(2020, 1, 1, 8, 30, 0, TimeSpan.FromHours(-3)), new DateTime(2026, 2, 3), typeof(DateTimeOffset));
        Assert.Equal(new DateTimeOffset(2026, 2, 3, 8, 30, 0, TimeSpan.FromHours(-3)), o);
    }

    [Theory]
    [InlineData("", typeof(int), true)]
    [InlineData("-12", typeof(int), true)]
    [InlineData("12-", typeof(int), false)]
    [InlineData("1,5", typeof(int), false)]
    [InlineData("1,5", typeof(decimal), true)]
    [InlineData("1,5,", typeof(decimal), false)]
    [InlineData("1.234,5", typeof(decimal), true)]
    [InlineData("-1", typeof(byte), false)]
    [InlineData("1,5e-3", typeof(double), true)]
    [InlineData("1e3", typeof(decimal), false)]
    [InlineData("abc", typeof(double), false)]
    public void NumericInputFilter(string text, Type type, bool ok)
        => Assert.Equal(ok, FormFieldLayout.IsAcceptableNumericInput(text, type, Br));
}

public class TextMetricsTests
{
    [Fact]
    public void Measure_UsesHelveticaWidths()
    {
        Assert.Equal(0, TextMetrics.Measure("", 10));
        Assert.Equal(5.56, TextMetrics.Measure("0", 10), 3);
        Assert.Equal(2.22, TextMetrics.Measure("i", 10), 3);
        Assert.Equal(TextMetrics.Measure("a", 10), TextMetrics.Measure("á", 10), 3);
        Assert.Equal(TextMetrics.Measure("C", 10), TextMetrics.Measure("Ç", 10), 3);
        Assert.True(TextMetrics.Measure("ABC", 10, bold: true) > TextMetrics.Measure("ABC", 10));
    }

    [Fact]
    public void Fit_TrimsWithEllipsis()
    {
        Assert.Equal("curto", TextMetrics.Fit("curto", 100, 8));
        var fit = TextMetrics.Fit("ADRIANA SERAFIM DA SILVA GAMA", 50, 8);
        Assert.EndsWith(TextMetrics.Ellipsis, fit);
        Assert.True(TextMetrics.Measure(fit, 8) <= 50);
        Assert.Equal("", TextMetrics.Fit("xyz", 1, 8));
        Assert.Equal("", TextMetrics.Fit(null, 10, 8));
    }
}

public class PrintLayoutTests
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static PrintTable Table(int rows, int cols = 3, string cell = "valor")
    {
        var data = Enumerable.Range(0, rows).Select(r => Enumerable.Range(0, cols).Select(c => (object?)(c == 0 ? r : cell)).ToArray()).ToList();
        return PrintTable.Build(
            Enumerable.Range(0, cols).Select(c => "COL" + c).ToArray(),
            Enumerable.Range(0, cols).Select(c => c == 0).ToArray(),
            new bool[cols],
            data, false, Br, "[dbo].[T] · footer");
    }

    [Fact]
    public void Build_FormatsCellsAndReportsProgress()
    {
        var reported = new List<int>();
        var t = PrintTable.Build(new[] { "A", "B" }, new[] { true, false }, new[] { false, true },
            new List<object?[]> { new object?[] { 1.5m, "memo" }, new object?[] { DBNull.Value, null } },
            false, Br, "f", new SyncProgress(reported));
        Assert.Equal(new[] { "1,5", "(MEMO)" }, t.Rows[0]);
        Assert.Equal(new[] { "", "" }, t.Rows[1]);
        Assert.Contains(2, reported);
    }

    [Fact]
    public void Build_Cancels()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => PrintTable.Build(new[] { "A" }, new[] { false }, new[] { false },
            new List<object?[]> { new object?[] { 1 } }, false, Br, "", null, cts.Token));
    }

    [Fact]
    public void Paginates_RowsPerPage_AndHeaderOnEveryPage()
    {
        var layout = PrintLayout.Create(Table(1899), new PrintSettings());
        Assert.True(layout.RowsPerPage > 40 && layout.RowsPerPage < 80, layout.RowsPerPage.ToString());
        Assert.Equal((int)Math.Ceiling(1899 / (double)layout.RowsPerPage), layout.PageCount);
        Assert.Single(layout.Bands);

        var last = layout.GetPage(layout.PageCount - 1);
        Assert.Equal(1899 - (layout.PageCount - 1) * layout.RowsPerPage, last.RowCount);
        for (var i = 0; i < layout.PageCount; i += layout.PageCount - 1)
        {
            var texts = layout.GetPage(i).Elements.OfType<PrintText>().ToList();
            Assert.Contains(texts, t => t.Bold && t.Text == "COL0");
            Assert.Contains(texts, t => t.Text == layout.PageLabel(i));
        }

        // Every element stays inside the page.
        foreach (var e in layout.GetPage(0).Elements)
        {
            if (e is PrintText t) Assert.True(t.X >= 0 && t.X + t.Width <= layout.PageWidth + 0.01 && t.Y + t.Height <= layout.PageHeight);
            if (e is PrintLine l) Assert.True(l.Y2 <= layout.PageHeight - layout.Settings.Margin);
        }
    }

    [Fact]
    public void EmptyTable_HasOnePageWithHeader()
    {
        var layout = PrintLayout.Create(Table(0), new PrintSettings());
        Assert.Equal(1, layout.PageCount);
        var page = layout.GetPage(0);
        Assert.Equal(0, page.RowCount);
        Assert.Contains(page.Elements.OfType<PrintText>(), t => t.Text == "COL2");
    }

    [Fact]
    public void Title_PrintedOnlyWhenSet()
    {
        var plain = PrintLayout.Create(Table(10), new PrintSettings());
        var titled = PrintLayout.Create(Table(10), new PrintSettings { Title = "Auditoria abril/2011" });
        Assert.Equal(0, plain.TitleHeight);
        Assert.True(titled.TitleHeight > 0);
        Assert.True(titled.RowsPerPage < plain.RowsPerPage);
        Assert.Contains(titled.GetPage(0).Elements.OfType<PrintText>(), t => t.Text == "Auditoria abril/2011" && t.Bold);
    }

    [Fact]
    public void NarrowColumns_KeepNaturalWidth()
    {
        var layout = PrintLayout.Create(Table(5), new PrintSettings());
        Assert.True(layout.ColumnWidths.Sum() < layout.PrintableWidth);
        var cellWidth = TextMetrics.Measure("valor", 8) + 6;
        Assert.True(layout.ColumnWidths[1] >= cellWidth - 0.01);
    }

    [Fact]
    public void WideColumns_ShrinkToFitAndTrim()
    {
        var layout = PrintLayout.Create(Table(5, 4, new string('W', 200)), new PrintSettings());
        Assert.Single(layout.Bands);
        Assert.True(layout.ColumnWidths.Sum() <= layout.PrintableWidth + 0.05);
        var page = layout.GetPage(0);
        Assert.Contains(page.Elements.OfType<PrintText>(), t => t.Text.EndsWith(TextMetrics.Ellipsis));
        foreach (var t in page.Elements.OfType<PrintText>())
            Assert.True(TextMetrics.Measure(t.Text, t.FontSize, t.Bold) <= t.Width + 0.01, t.Text);
    }

    [Fact]
    public void ManyColumns_SplitIntoBands()
    {
        var layout = PrintLayout.Create(Table(100, 40, "texto razoavelmente comprido"), new PrintSettings());
        Assert.True(layout.Bands.Count > 1);
        Assert.Equal(40, layout.Bands.Sum(b => b.Count));
        Assert.Equal(layout.RowChunks * layout.Bands.Count, layout.PageCount);
        foreach (var (first, count) in layout.Bands)
            Assert.True(layout.ColumnWidths.Skip(first).Take(count).Sum() <= layout.PrintableWidth + 0.05);
        Assert.Contains("colunas", layout.PageLabel(1));
        Assert.Equal(1, layout.GetPage(1).Band);
        Assert.Equal(0, layout.GetPage(1).FirstRow);
    }

    [Fact]
    public void Landscape_SwapsPageAndFitsMoreColumns()
    {
        var portrait = PrintLayout.Create(Table(10, 40, "texto razoavelmente comprido"), new PrintSettings());
        var landscape = PrintLayout.Create(Table(10, 40, "texto razoavelmente comprido"), new PrintSettings { Landscape = true });
        Assert.True(landscape.PageWidth > landscape.PageHeight);
        Assert.True(landscape.Bands.Count <= portrait.Bands.Count);
        Assert.True(landscape.RowsPerPage < portrait.RowsPerPage);
    }

    [Fact]
    public void NumericColumnsAlignRight()
    {
        var layout = PrintLayout.Create(Table(3), new PrintSettings());
        var cells = layout.GetPage(0).Elements.OfType<PrintText>().Where(t => !t.Bold).ToList();
        Assert.Contains(cells, t => t.Text == "2" && t.Align == PrintTextAlign.Right);
        Assert.Contains(cells, t => t.Text == "valor" && t.Align == PrintTextAlign.Left);
    }

    [Fact]
    public void GetPage_OutOfRangeThrows()
        => Assert.Throws<ArgumentOutOfRangeException>(() => PrintLayout.Create(Table(1), new PrintSettings()).GetPage(1));

    private sealed class SyncProgress : IProgress<int>
    {
        private readonly List<int> _list;
        public SyncProgress(List<int> list) => _list = list;
        public void Report(int value) => _list.Add(value);
    }
}

public class PdfTableWriterTests
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static PrintLayout Layout(int rows, string title = "Título (teste)")
    {
        var data = Enumerable.Range(0, rows).Select(r => new object?[] { r, "Ação (çã) \\ fim", DBNull.Value }).ToList();
        var table = PrintTable.Build(new[] { "ID", "NOME", "OBS" }, new[] { true, false, false }, new[] { false, false, true }, data, false, Br, "rodapé");
        return PrintLayout.Create(table, new PrintSettings { Title = title });
    }

    [Fact]
    public void LiteralString_EscapesAndEncodesWinAnsi()
    {
        Assert.Equal("(a\\(b\\)\\\\c)", PdfTableWriter.LiteralString("a(b)\\c"));
        Assert.Equal("(\\347\\343o)", PdfTableWriter.LiteralString("ção"));
        Assert.Equal("(\\205)", PdfTableWriter.LiteralString(TextMetrics.Ellipsis));
        Assert.Equal("(?)", PdfTableWriter.LiteralString("中"));
    }

    [Fact]
    public void TextString_UsesUtf16ForNonAscii()
    {
        Assert.Equal("(abc)", PdfTableWriter.TextString("abc"));
        Assert.Equal("<FEFF00E7>", PdfTableWriter.TextString("ç"));
    }

    [Fact]
    public void Write_ProducesValidStructure()
    {
        var layout = Layout(300);
        using var ms = new MemoryStream();
        var pages = new List<int>();
        PdfTableWriter.Write(ms, layout, "Auditoria", new DateTime(2026, 10, 20, 9, 22, 0), new Progress(pages));
        var bytes = ms.ToArray();
        var text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains($"/Type /Pages /Count {layout.PageCount}", text);
        Assert.Equal(layout.PageCount, Regex.Matches(text, @"/Type /Page /Parent").Count);
        Assert.Contains("/BaseFont /Helvetica-Bold", text);
        Assert.Contains("/CreationDate (D:20261020092200", text);
        Assert.Equal(layout.PageCount, pages.Last());

        // xref offsets point at "n 0 obj".
        var startxref = long.Parse(Regex.Match(text, @"startxref\n(\d+)\n%%EOF").Groups[1].Value);
        Assert.StartsWith("xref", text[(int)startxref..]);
        var entries = Regex.Matches(text[(int)startxref..], @"(\d{10}) 00000 n ");
        Assert.Equal(5 + 2 * layout.PageCount, entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var off = int.Parse(entries[i].Groups[1].Value);
            Assert.StartsWith($"{i + 1} 0 obj", text[off..]);
        }

        // First content stream inflates to drawing operators with the escaped title and cells.
        var m = Regex.Match(text, @"/Length (\d+) /Filter /FlateDecode >>\nstream\n");
        var len = int.Parse(m.Groups[1].Value);
        var start = m.Index + m.Length;
        using var z = new ZLibStream(new MemoryStream(bytes, start, len), CompressionMode.Decompress);
        using var reader = new StreamReader(z, Encoding.Latin1);
        var content = reader.ReadToEnd();
        Assert.Contains("(T\\355tulo \\(teste\\)) Tj", content);
        Assert.Contains("re f", content);
        Assert.Contains(" l S", content);
        Assert.Contains("/F2 8 Tf", content);
        Assert.Contains("(A\\347\\343o \\(\\347\\343\\) \\\\ fim) Tj", content);
        Assert.Contains("(Página 1 de", PdfTableWriter.PageContent(layout.GetPage(0)).Replace("\\341", "á"));
    }

    [Fact]
    public void Write_Cancels()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var ms = new MemoryStream();
        Assert.Throws<OperationCanceledException>(() => PdfTableWriter.Write(ms, Layout(10), "x", DateTime.Now, null, cts.Token));
    }

    [Fact]
    public void PageContent_RightAlignsNumbers()
    {
        var layout = Layout(3, "");
        var page = layout.GetPage(0);
        var cell = page.Elements.OfType<PrintText>().First(t => t.Text == "2");
        var content = PdfTableWriter.PageContent(page);
        var expectedX = Math.Round(cell.X + cell.Width - TextMetrics.Measure("2", cell.FontSize), 2).ToString("0.##", CultureInfo.InvariantCulture);
        Assert.Contains($"{expectedX} ", content);
    }

    private sealed class Progress : IProgress<int>
    {
        private readonly List<int> _list;
        public Progress(List<int> list) => _list = list;
        public void Report(int value) => _list.Add(value);
    }
}
