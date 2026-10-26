using System.Text.RegularExpressions;
using SqlExpert.Core.DataForms;

namespace SqlExpert.Tests;

/// <summary>pt-BR input mask of date/time editors (dd/MM/yyyy HH:mm:ss). No GUI is started.</summary>
public class DateEditMaskTests
{
    private const DateMaskKind D = DateMaskKind.Date;
    private const DateMaskKind DT = DateMaskKind.DateTime;
    private const DateMaskKind T = DateMaskKind.Time;
    private const string EmptyDT = "__/__/____ __:__:__";

    // ===== kinds and templates =====

    [Theory]
    [InlineData(DateColumnKind.Date, typeof(DateTime), DateMaskKind.Date)]
    [InlineData(DateColumnKind.SmallDateTime, typeof(DateTime), DateMaskKind.DateTime)]
    [InlineData(DateColumnKind.DateTime, typeof(DateTime), DateMaskKind.DateTime)]
    [InlineData(DateColumnKind.DateTime2, typeof(DateTime), DateMaskKind.DateTime)]
    [InlineData(DateColumnKind.DateTimeOffset, typeof(DateTimeOffset), DateMaskKind.DateTime)]
    [InlineData(DateColumnKind.None, typeof(TimeSpan), DateMaskKind.Time)]
    [InlineData(DateColumnKind.None, typeof(string), DateMaskKind.None)]
    [InlineData(DateColumnKind.None, typeof(int), DateMaskKind.None)]
    public void KindOf_Column(DateColumnKind kind, Type clr, DateMaskKind expected) =>
        Assert.Equal(expected, DateEditMask.KindOf(kind, clr));

    [Fact]
    public void Templates_AndHints()
    {
        Assert.Equal("__/__/____", DateEditMask.Template(D));
        Assert.Equal(EmptyDT, DateEditMask.Template(DT));
        Assert.Equal("__:__:__", DateEditMask.Template(T));
        Assert.Equal("dd/mm/aaaa hh:mm:ss", DateEditMask.Hint(DT));
    }

    // ===== value → editor text =====

    [Fact]
    public void Format_ShowsOnlyThePtBrMask_WithoutFraction()
    {
        var d = new DateTime(2038, 4, 28, 14, 53, 13, 123).AddTicks(4567);
        Assert.Equal("28/04/2038 14:53:13", DateEditMask.Format(d, DT));
        Assert.Equal("28/04/2038", DateEditMask.Format(d.Date, D));
        Assert.Equal("28/04/2038 14:53:13", DateEditMask.Format(new DateTimeOffset(d, TimeSpan.FromHours(-3)), DT));
        Assert.Equal("14:53:13", DateEditMask.Format(new TimeSpan(0, 14, 53, 13, 500), T));
    }

    [Fact]
    public void Format_Null_IsTheEmptyMask()
    {
        Assert.Equal(EmptyDT, DateEditMask.Format(DBNull.Value, DT));
        Assert.Equal(EmptyDT, DateEditMask.Format(null, DT));
        Assert.Equal("__:__:__", DateEditMask.Format(DBNull.Value, T));
    }

    [Theory]
    [InlineData("", EmptyDT)]
    [InlineData("2", "2_/__/____ __:__:__")]
    [InlineData("28042038", "28/04/2038 __:__:__")]
    [InlineData("28/04/2038 14:53:13", "28/04/2038 14:53:13")]
    [InlineData("28/04/2038 1_:__:__", "28/04/2038 1_:__:__")]
    [InlineData("abc", EmptyDT)]
    public void Normalize_PutsTextInMaskShape(string text, string expected) =>
        Assert.Equal(expected, DateEditMask.Normalize(text, DT));

    // ===== keys =====

    [Fact]
    public void Typing_FillsThePositions_AndTheCaretSkipsTheSeparators()
    {
        var edit = new MaskEdit(DateEditMask.Template(DT), 0);
        foreach (var ch in "28042038145313")
            edit = DateEditMask.Type(edit.Text, edit.Caret, 0, ch, DT);
        Assert.Equal("28/04/2038 14:53:13", edit.Text);
        Assert.Equal(19, edit.Caret);

        var two = DateEditMask.Type(EmptyDT, 0, 0, '2', DT);
        two = DateEditMask.Type(two.Text, two.Caret, 0, '8', DT);
        Assert.Equal("28/__/____ __:__:__", two.Text);
        Assert.Equal(3, two.Caret); // after the "/"
    }

    [Fact]
    public void Typing_NonDigits_IsIgnored()
    {
        var edit = DateEditMask.Type(EmptyDT, 0, 0, 'a', DT);
        Assert.Equal(EmptyDT, edit.Text);
        Assert.Equal(0, edit.Caret);
        Assert.Equal("__/__/____ __:__:__", DateEditMask.Type(EmptyDT, 0, 0, '-', DT).Text);
    }

    [Fact]
    public void Typing_OverwritesInPlace_AndReplacesTheSelection()
    {
        var edit = DateEditMask.Type("28/04/2038 14:53:13", 0, 0, '1', DT);
        Assert.Equal("18/04/2038 14:53:13", edit.Text);
        Assert.Equal(1, edit.Caret);

        var all = DateEditMask.Type("28/04/2038 14:53:13", 0, 19, '0', DT);
        Assert.Equal("0_/__/____ __:__:__", all.Text);
        Assert.Equal(1, all.Caret);
    }

    [Fact]
    public void Separator_JumpsToTheNextField_WithLeadingZero()
    {
        var edit = DateEditMask.Type(EmptyDT, 0, 0, '5', DT);
        edit = DateEditMask.Type(edit.Text, edit.Caret, 0, '/', DT);
        Assert.Equal("05/__/____ __:__:__", edit.Text);
        Assert.Equal(3, edit.Caret);

        var hour = DateEditMask.Type("28/04/2038 9_:__:__", 12, 0, ':', DT);
        Assert.Equal("28/04/2038 09:__:__", hour.Text);
        Assert.Equal(14, hour.Caret);

        var space = DateEditMask.Type("28/04/2038 __:__:__", 10, 0, ' ', DT);
        Assert.Equal(11, space.Caret);
    }

    [Fact]
    public void Backspace_ClearsThePreviousPosition_SkippingSeparators()
    {
        var edit = DateEditMask.Backspace("28/04/2038 14:53:13", 3, 0, DT);
        Assert.Equal("2_/04/2038 14:53:13", edit.Text);
        Assert.Equal(1, edit.Caret);

        var sel = DateEditMask.Backspace("28/04/2038 14:53:13", 0, 19, DT);
        Assert.Equal(EmptyDT, sel.Text);
        Assert.Equal(0, sel.Caret);

        Assert.Equal(0, DateEditMask.Backspace(EmptyDT, 0, 0, DT).Caret);
    }

    [Fact]
    public void Delete_ClearsThePositionAtTheCaret_AndKeepsTheCaret()
    {
        var edit = DateEditMask.Delete("28/04/2038 14:53:13", 2, 0, DT);
        Assert.Equal("28/_4/2038 14:53:13", edit.Text);
        Assert.Equal(2, edit.Caret);

        var range = DateEditMask.Delete("28/04/2038 14:53:13", 11, 8, DT);
        Assert.Equal("28/04/2038 __:__:__", range.Text);
    }

    [Fact]
    public void Paste_FillsThePositionsWithTheDigits()
    {
        var all = DateEditMask.Paste("01/01/2000 00:00:00", 0, 19, "28/04/2038 14:53:13", DT);
        Assert.Equal("28/04/2038 14:53:13", all.Text);
        Assert.Equal(19, all.Caret);

        var time = DateEditMask.Paste("28/04/2038 __:__:__", 11, 0, "14h53", DT);
        Assert.Equal("28/04/2038 14:53:__", time.Text);
        Assert.Equal(17, time.Caret);

        // ISO text is not accepted as such: its digits go into the pt-BR positions (invalid date → red border).
        var iso = DateEditMask.Paste(EmptyDT, 0, 19, "2038-04-28 14:53:13", DT);
        Assert.Equal("20/38/0428 14:53:13", iso.Text);
        Assert.NotNull(DateEditMask.LiveError(iso.Text, DT, DateColumnKind.DateTime, typeof(DateTime)));

        Assert.Equal(EmptyDT, DateEditMask.Paste(EmptyDT, 0, 0, "abc", DT).Text);
    }

    [Fact]
    public void Cut_ClearsTheSelection()
    {
        Assert.Equal("__/__/____ 14:53:13", DateEditMask.Cut("28/04/2038 14:53:13", 0, 10, DT).Text);
        Assert.Equal("28/04/2038 14:53:13", DateEditMask.Cut("28/04/2038 14:53:13", 4, 0, DT).Text);
    }

    // ===== editor text → value =====

    [Fact]
    public void Parse_ValidMask()
    {
        Assert.Equal(new DateTime(2038, 4, 28, 14, 53, 13), DateEditMask.Parse("28/04/2038 14:53:13", DT, DateColumnKind.DateTime, typeof(DateTime)));
        Assert.Equal(new DateTime(2038, 4, 28), DateEditMask.Parse("28/04/2038", D, DateColumnKind.Date, typeof(DateTime)));
        Assert.Equal(new TimeSpan(14, 53, 13), DateEditMask.Parse("14:53:13", T, DateColumnKind.None, typeof(TimeSpan)));
        Assert.Equal(new DateTime(2024, 2, 29), DateEditMask.Parse("29/02/2024", D, DateColumnKind.Date, typeof(DateTime)));
    }

    [Fact]
    public void Parse_EmptyMask_IsNull()
    {
        Assert.Equal(DBNull.Value, DateEditMask.Parse(EmptyDT, DT, DateColumnKind.DateTime, typeof(DateTime)));
        Assert.Equal(DBNull.Value, DateEditMask.Parse("", T, DateColumnKind.None, typeof(TimeSpan)));
    }

    [Theory]
    [InlineData("28/04/2038 14:53:__", "incompleta")]
    [InlineData("31/02/2038 00:00:00", "inválida")]
    [InlineData("29/02/2023 00:00:00", "inválida")]
    [InlineData("00/04/2038 00:00:00", "inválida")]
    [InlineData("28/13/2038 00:00:00", "inválida")]
    [InlineData("28/04/0000 00:00:00", "inválida")]
    [InlineData("28/04/2038 24:00:00", "Hora inválida")]
    [InlineData("28/04/2038 14:60:00", "Hora inválida")]
    [InlineData("28/04/2038 14:53:60", "Hora inválida")]
    [InlineData("2038-04-28 14:53:13", "inválida")]
    public void Parse_IncompleteOrInvalid_IsRefused(string text, string message)
    {
        var ex = Assert.Throws<FormatException>(() => DateEditMask.Parse(text, DT, DateColumnKind.DateTime2, typeof(DateTime)));
        Assert.Contains(message, ex.Message);
        Assert.False(DateEditMask.TryParse(text, DT, DateColumnKind.DateTime2, typeof(DateTime), null, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_Time_OutOfRange_IsRefused()
    {
        Assert.Throws<FormatException>(() => DateEditMask.Parse("24:00:00", T, DateColumnKind.None, typeof(TimeSpan)));
        Assert.Throws<FormatException>(() => DateEditMask.Parse("12:3_:__", T, DateColumnKind.None, typeof(TimeSpan)));
    }

    [Fact]
    public void Parse_RangesOfSmallDateTimeAndDateTime()
    {
        Assert.Throws<FormatException>(() => DateEditMask.Parse("31/12/1899 23:59:59", DT, DateColumnKind.SmallDateTime, typeof(DateTime)));
        Assert.Throws<FormatException>(() => DateEditMask.Parse("07/06/2079 00:00:00", DT, DateColumnKind.SmallDateTime, typeof(DateTime)));
        Assert.Equal(new DateTime(2079, 6, 6, 23, 59, 0), DateEditMask.Parse("06/06/2079 23:59:00", DT, DateColumnKind.SmallDateTime, typeof(DateTime)));
        Assert.Throws<FormatException>(() => DateEditMask.Parse("31/12/1752 00:00:00", DT, DateColumnKind.DateTime, typeof(DateTime)));
        Assert.Equal(new DateTime(1753, 1, 1), DateEditMask.Parse("01/01/1753 00:00:00", DT, DateColumnKind.DateTime, typeof(DateTime)));
        Assert.Equal(new DateTime(1, 1, 1), DateEditMask.Parse("01/01/0001 00:00:00", DT, DateColumnKind.DateTime2, typeof(DateTime)));
    }

    [Fact]
    public void Parse_KeepsTheFraction_WhenTheSecondsAreUnchanged_ElseZero()
    {
        var original = new DateTime(2038, 4, 28, 14, 53, 13, 123).AddTicks(4567);
        Assert.Equal(original, DateEditMask.Parse("28/04/2038 14:53:13", DT, DateColumnKind.DateTime2, typeof(DateTime), original));
        Assert.Equal(new DateTime(2038, 4, 28, 14, 53, 14), DateEditMask.Parse("28/04/2038 14:53:14", DT, DateColumnKind.DateTime2, typeof(DateTime), original));
        Assert.Equal(new DateTime(2038, 4, 29, 14, 53, 13), DateEditMask.Parse("29/04/2038 14:53:13", DT, DateColumnKind.DateTime2, typeof(DateTime), original));

        var time = new TimeSpan(0, 14, 53, 13, 250);
        Assert.Equal(time, DateEditMask.Parse("14:53:13", T, DateColumnKind.None, typeof(TimeSpan), time));
        Assert.Equal(new TimeSpan(14, 53, 0), DateEditMask.Parse("14:53:00", T, DateColumnKind.None, typeof(TimeSpan), time));
    }

    [Fact]
    public void Parse_DateTimeOffset_KeepsTheOriginalOffset()
    {
        var original = new DateTimeOffset(2038, 4, 28, 14, 53, 13, 500, TimeSpan.FromHours(5.5));
        Assert.Equal(original, DateEditMask.Parse("28/04/2038 14:53:13", DT, DateColumnKind.DateTimeOffset, typeof(DateTimeOffset), original));

        var changed = (DateTimeOffset)DateEditMask.Parse("01/05/2038 08:00:00", DT, DateColumnKind.DateTimeOffset, typeof(DateTimeOffset), original);
        Assert.Equal(new DateTime(2038, 5, 1, 8, 0, 0), changed.DateTime);
        Assert.Equal(TimeSpan.FromHours(5.5), changed.Offset);

        var fromNull = (DateTimeOffset)DateEditMask.Parse("01/05/2038 08:00:00", DT, DateColumnKind.DateTimeOffset, typeof(DateTimeOffset), DBNull.Value);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(new DateTime(2038, 5, 1, 8, 0, 0)), fromNull.Offset);
    }

    [Fact]
    public void RoundTrip_FormatThenParse_GivesTheOriginalBack()
    {
        var values = new object[]
        {
            new DateTime(2037, 10, 24, 11, 42, 51, 997),
            new DateTimeOffset(2037, 10, 24, 11, 42, 51, TimeSpan.FromHours(-3)).AddTicks(1234567),
        };
        Assert.Equal(values[0], DateEditMask.Parse(DateEditMask.Format(values[0], DT), DT, DateColumnKind.DateTime, typeof(DateTime), values[0]));
        Assert.Equal(values[1], DateEditMask.Parse(DateEditMask.Format(values[1], DT), DT, DateColumnKind.DateTimeOffset, typeof(DateTimeOffset), values[1]));
        // display stays ISO
        Assert.Equal("2037-10-24 11:42:51.997", DateValueText.Format(values[0], DateColumnKind.DateTime, 3));
    }

    [Fact]
    public void LiveError_OnlyForACompleteInvalidText()
    {
        Assert.Null(DateEditMask.LiveError("31/02/20__ __:__:__", DT, DateColumnKind.DateTime, typeof(DateTime)));
        Assert.NotNull(DateEditMask.LiveError("31/02/2038 00:00:00", DT, DateColumnKind.DateTime, typeof(DateTime)));
        Assert.Null(DateEditMask.LiveError("28/02/2038 00:00:00", DT, DateColumnKind.DateTime, typeof(DateTime)));
        Assert.Null(DateEditMask.LiveError(EmptyDT, DT, DateColumnKind.DateTime, typeof(DateTime)));
    }

    // ===== wiring (source checks, no GUI) =====

    private static string Views()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "SqlExpert.App", "Views"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new DirectoryNotFoundException("SqlExpert.App/Views"), "SqlExpert.App", "Views");
    }

    private static string Src(string file) => File.ReadAllText(Path.Combine(Views(), file)).Replace("\r\n", "\n");

    [Fact]
    public void Editors_UseTheMask_AndTheGridRefusesAnInvalidCommit()
    {
        var column = Src("NullAwareTextColumn.cs");
        Assert.Contains("DateMaskBehavior.Attach(tb, MaskKind, DateKind, DataType)", column);

        var conv = Src("CellValueConverters.cs");
        var editor = Regex.Match(conv, @"class CellEditorValueConverter[\s\S]*?\n}\n").Value;
        Assert.Contains("DateEditMask.Format(value, _mask)", editor);
        Assert.Contains("DateEditMask.Parse(text, _mask, _dateKind, _type, _original)", editor);
        Assert.DoesNotContain("DateValueText.Parse", editor);

        var grid = Src("TableDataGrid.xaml.cs");
        Assert.Contains("Grid.CellEditEnding += Grid_OnCellEditEnding", grid);
        Assert.Contains("e.Cancel = true;", grid);
        Assert.Contains("DateMaskBehavior.ShowErrorLater(tb, error)", grid);
        Assert.Contains("tb.Text = DateEditMask.Format(value, column.MaskKind)", grid); // calendar writes the mask

        var form = Src("TableDataGrid.Form.cs");
        Assert.Contains("AttachFormMask(tb, converter, dateKind, spec.ClrType)", form);
        Assert.Contains("converter.Editing = true", form);
        Assert.Contains("converter.Editing = false", form);

        var behavior = Src("DateMaskBehavior.cs");
        Assert.Contains("MessageDialog.Show(", behavior);
        Assert.DoesNotContain("MessageBox.Show(", behavior);
    }
}
