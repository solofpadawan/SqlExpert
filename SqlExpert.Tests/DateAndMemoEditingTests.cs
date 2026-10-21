using System.Globalization;
using SqlExpert.Core.DataForms;
using SqlExpert.Core.Models;

namespace SqlExpert.Tests;

/// <summary>Date columns (pt-BR text + calendar rules) and the "..." memo window rules. No GUI is started.</summary>
public class DateValueTextTests
{
    private static DataColumnInfo Col(string type, int maxLength = 8, bool nullable = true, string baseType = "") =>
        new() { Name = "C", TypeName = type, BaseTypeName = baseType, MaxLength = maxLength, IsNullable = nullable };

    // ===== type detection =====

    [Theory]
    [InlineData("date", DateColumnKind.Date)]
    [InlineData("DATE", DateColumnKind.Date)]
    [InlineData("smalldatetime", DateColumnKind.SmallDateTime)]
    [InlineData("datetime", DateColumnKind.DateTime)]
    [InlineData("datetime2", DateColumnKind.DateTime2)]
    [InlineData("datetime2(3)", DateColumnKind.DateTime2)]
    [InlineData("datetimeoffset", DateColumnKind.DateTimeOffset)]
    [InlineData("time", DateColumnKind.None)]
    [InlineData("varchar", DateColumnKind.None)]
    [InlineData("int", DateColumnKind.None)]
    [InlineData("", DateColumnKind.None)]
    public void KindOf_TypeName(string type, DateColumnKind expected) => Assert.Equal(expected, DateValueText.KindOf(type));

    [Fact]
    public void KindOf_AliasType_UsesTheBaseType()
    {
        Assert.Equal(DateColumnKind.Date, DateValueText.KindOf(Col("D_DATA", baseType: "date")));
        Assert.Equal(DateColumnKind.DateTime, DateValueText.KindOf(Col("datetime")));
        Assert.False(DateValueText.HasTime(DateColumnKind.Date));
        Assert.True(DateValueText.HasTime(DateColumnKind.SmallDateTime));
        Assert.True(DateValueText.HasTime(DateColumnKind.DateTimeOffset));
    }

    // ===== formatting (pt-BR) =====

    [Fact]
    public void Format_PtBr_DdMMyyyy_PlusTimeForDateTimeTypes()
    {
        var d = new DateTime(2011, 4, 25, 14, 5, 9);
        Assert.Equal("25/04/2011", DateValueText.Format(new DateTime(2011, 4, 25), DateColumnKind.Date));
        Assert.Equal("25/04/2011", DateValueText.Format(d, DateColumnKind.Date));
        Assert.Equal("25/04/2011 14:05", DateValueText.Format(d, DateColumnKind.SmallDateTime));
        Assert.Equal("25/04/2011 14:05:09", DateValueText.Format(d, DateColumnKind.DateTime));
        Assert.Equal("25/04/2011 00:00:00", DateValueText.Format(new DateTime(2011, 4, 25), DateColumnKind.DateTime));
        Assert.Equal("", DateValueText.Format(DBNull.Value, DateColumnKind.DateTime));
        Assert.Equal("", DateValueText.Format(null, DateColumnKind.Date));
    }

    [Fact]
    public void Format_KeepsFullPrecision_ForDateTime2AndOffset()
    {
        var d = new DateTime(2026, 10, 21, 9, 30, 15).AddTicks(1234567);
        Assert.Equal("21/10/2026 09:30:15.1234567", DateValueText.Format(d, DateColumnKind.DateTime2));
        Assert.Equal("21/10/2026 09:30:15.5", DateValueText.Format(new DateTime(2026, 10, 21, 9, 30, 15, 500), DateColumnKind.DateTime));
        var o = new DateTimeOffset(d, TimeSpan.FromHours(-3));
        Assert.Equal("21/10/2026 09:30:15.1234567 -03:00", DateValueText.Format(o, DateColumnKind.DateTimeOffset));
    }

    [Fact]
    public void Format_ThenParse_RoundTrips_WithoutLosingPrecision()
    {
        var d = new DateTime(2026, 10, 21, 9, 30, 15).AddTicks(1234567);
        Assert.Equal(d, DateValueText.Parse(DateValueText.Format(d, DateColumnKind.DateTime2), DateColumnKind.DateTime2, typeof(DateTime)));
        var o = new DateTimeOffset(d, TimeSpan.FromHours(5.5));
        var back = (DateTimeOffset)DateValueText.Parse(DateValueText.Format(o, DateColumnKind.DateTimeOffset), DateColumnKind.DateTimeOffset, typeof(DateTimeOffset));
        Assert.Equal(o, back);
        Assert.Equal(o.Offset, back.Offset);
    }

    // ===== parsing =====

    [Theory]
    [InlineData("25/04/2011")]
    [InlineData("25/4/2011")]
    [InlineData("2011-04-25")]
    [InlineData(" 25/04/2011 ")]
    public void Parse_Date_PtBrAndIso(string text) =>
        Assert.Equal(new DateTime(2011, 4, 25), DateValueText.Parse(text, DateColumnKind.Date, typeof(DateTime)));

    [Theory]
    [InlineData("25/04/2011 14:05", 0)]
    [InlineData("25/04/2011 14:05:09", 9)]
    [InlineData("2011-04-25 14:05:09", 9)]
    [InlineData("2011-04-25T14:05:09", 9)]
    public void Parse_DateTime_WithTime(string text, int seconds) =>
        Assert.Equal(new DateTime(2011, 4, 25, 14, 5, seconds), DateValueText.Parse(text, DateColumnKind.DateTime, typeof(DateTime)));

    [Fact]
    public void Parse_DateTime_DateOnly_IsMidnight() =>
        Assert.Equal(new DateTime(2011, 4, 25), DateValueText.Parse("25/04/2011", DateColumnKind.DateTime2, typeof(DateTime)));

    [Theory]
    [InlineData("04/25/2011")]   // US order: month 25 does not exist
    [InlineData("31/02/2011")]
    [InlineData("abc")]
    [InlineData("25/04/2011 25:00")]
    [InlineData("2011-13-01")]
    public void Parse_Invalid_Throws(string text) =>
        Assert.Throws<FormatException>(() => DateValueText.Parse(text, DateColumnKind.DateTime, typeof(DateTime)));

    [Fact]
    public void Parse_DateColumn_RejectsATimePart() =>
        Assert.Throws<FormatException>(() => DateValueText.Parse("25/04/2011 10:00", DateColumnKind.Date, typeof(DateTime)));

    [Fact]
    public void Parse_Blank_IsNull()
    {
        Assert.Equal(DBNull.Value, DateValueText.Parse("", DateColumnKind.Date, typeof(DateTime)));
        Assert.Equal(DBNull.Value, DateValueText.Parse("  ", DateColumnKind.DateTimeOffset, typeof(DateTimeOffset)));
    }

    [Fact]
    public void Parse_TypeRanges_SmallDateTimeAndDateTime()
    {
        Assert.Throws<FormatException>(() => DateValueText.Parse("01/01/1899 00:00", DateColumnKind.SmallDateTime, typeof(DateTime)));
        Assert.Throws<FormatException>(() => DateValueText.Parse("01/01/1700", DateColumnKind.DateTime, typeof(DateTime)));
        Assert.Equal(new DateTime(1700, 1, 1), DateValueText.Parse("01/01/1700", DateColumnKind.DateTime2, typeof(DateTime)));
    }

    [Fact]
    public void Parse_Offset_ExplicitOrLocal()
    {
        var o = (DateTimeOffset)DateValueText.Parse("21/10/2026 09:30:00 -03:00", DateColumnKind.DateTimeOffset, typeof(DateTimeOffset));
        Assert.Equal(TimeSpan.FromHours(-3), o.Offset);
        Assert.Equal(new DateTime(2026, 10, 21, 9, 30, 0), o.DateTime);
        var local = (DateTimeOffset)DateValueText.Parse("21/10/2026", DateColumnKind.DateTimeOffset, typeof(DateTimeOffset));
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 21)), local.Offset);
    }

    // ===== calendar: time preservation, Today, Clear =====

    [Fact]
    public void PickDay_DateTime_KeepsTheTimePart()
    {
        var current = new DateTime(2011, 4, 25, 14, 5, 9).AddTicks(42);
        Assert.Equal(new DateTime(2011, 5, 3, 14, 5, 9).AddTicks(42), DateValueText.PickDay(current, new DateTime(2011, 5, 3, 23, 59, 0), DateColumnKind.DateTime2));
    }

    [Fact]
    public void PickDay_Null_GetsMidnight()
    {
        Assert.Equal(new DateTime(2011, 5, 3), DateValueText.PickDay(DBNull.Value, new DateTime(2011, 5, 3), DateColumnKind.DateTime));
        Assert.Equal(new DateTime(2011, 5, 3), DateValueText.PickDay(null, new DateTime(2011, 5, 3, 8, 0, 0), DateColumnKind.Date));
    }

    [Fact]
    public void PickDay_Date_DropsAnyTime() =>
        Assert.Equal(new DateTime(2011, 5, 3), DateValueText.PickDay(new DateTime(2011, 4, 25, 10, 0, 0), new DateTime(2011, 5, 3), DateColumnKind.Date));

    [Fact]
    public void PickDay_Offset_KeepsTimeAndOffset()
    {
        var current = new DateTimeOffset(2011, 4, 25, 14, 5, 9, TimeSpan.FromHours(2));
        var picked = (DateTimeOffset)DateValueText.PickDay(current, new DateTime(2011, 5, 3), DateColumnKind.DateTimeOffset);
        Assert.Equal(new DateTimeOffset(2011, 5, 3, 14, 5, 9, TimeSpan.FromHours(2)), picked);
        Assert.Equal(TimeSpan.FromHours(2), picked.Offset);
        var fromNull = (DateTimeOffset)DateValueText.PickDay(DBNull.Value, new DateTime(2011, 5, 3), DateColumnKind.DateTimeOffset);
        Assert.Equal(new DateTime(2011, 5, 3), fromNull.DateTime);
    }

    [Fact]
    public void Today_KeepsTime_OrMidnight()
    {
        var today = new DateTime(2026, 10, 21);
        Assert.Equal(new DateTime(2026, 10, 21, 14, 5, 9), DateValueText.Today(new DateTime(2011, 4, 25, 14, 5, 9), DateColumnKind.DateTime, today));
        Assert.Equal(today, DateValueText.Today(DBNull.Value, DateColumnKind.DateTime, today));
        Assert.Equal(today, DateValueText.Today(new DateTime(2011, 4, 25), DateColumnKind.Date, today));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void Clear_OnlyForNullableEditableColumns(bool nullable, bool readOnly, bool expected) =>
        Assert.Equal(expected, DateValueText.CanClear(nullable, readOnly));

    [Fact]
    public void Clear_OnANullCell_LeavesItUntouched()
    {
        // Clear puts an empty text in the editor: NULL left blank stays NULL (no change), a value becomes NULL.
        Assert.True(DataValueText.KeepsNullWhenBlank(DBNull.Value, ""));
        Assert.False(DataValueText.KeepsNullWhenBlank(new DateTime(2011, 4, 25), ""));
        Assert.Equal(DBNull.Value, DateValueText.Parse("", DateColumnKind.DateTime, typeof(DateTime)));
    }

    [Fact]
    public void SelectedDate_IsTheDatePart()
    {
        Assert.Equal(new DateTime(2011, 4, 25), DateValueText.SelectedDate(new DateTime(2011, 4, 25, 10, 0, 0)));
        Assert.Equal(new DateTime(2011, 4, 25), DateValueText.SelectedDate(new DateTimeOffset(2011, 4, 25, 23, 0, 0, TimeSpan.FromHours(-3))));
        Assert.Null(DateValueText.SelectedDate(DBNull.Value));
    }

    // ===== calendar layout =====

    [Fact]
    public void DayInitials_PtBr_AreDSTQQSS() =>
        Assert.Equal(new[] { "d", "s", "t", "q", "q", "s", "s" }, DateValueText.DayInitials());

    [Fact]
    public void MonthTitle_PtBr() => Assert.Equal("abril 2011", DateValueText.MonthTitle(2011, 4));

    [Fact]
    public void MonthDays_SixWeeks_FromSunday_WithAdjacentMonths()
    {
        var days = DateValueText.MonthDays(2011, 4); // 01/04/2011 is a Friday
        Assert.Equal(42, days.Count);
        Assert.Equal(new DateTime(2011, 3, 27), days[0]);
        Assert.Equal(DayOfWeek.Sunday, days[0].DayOfWeek);
        Assert.Equal(new DateTime(2011, 4, 1), days[5]);
        Assert.Equal(new DateTime(2011, 5, 7), days[^1]);
        Assert.Equal(30, days.Count(d => d.Month == 4));
    }
}

public class MemoEditRulesTests
{
    private static DataColumnInfo Col(string type, int maxLength, string baseType = "") =>
        new() { Name = "OBS", TypeName = type, BaseTypeName = baseType, MaxLength = maxLength, IsNullable = true };

    [Theory]
    [InlineData("char", true)]
    [InlineData("varchar", true)]
    [InlineData("nchar", true)]
    [InlineData("nvarchar", true)]
    [InlineData("NVARCHAR(MAX)", true)]
    [InlineData("varchar(255)", true)]
    [InlineData("text", true)]
    [InlineData("ntext", true)]
    [InlineData("xml", false)]
    [InlineData("int", false)]
    [InlineData("datetime", false)]
    [InlineData("varbinary", false)]
    [InlineData("uniqueidentifier", false)]
    [InlineData(null, false)]
    public void IsTextTypeName(string? type, bool expected) => Assert.Equal(expected, MemoEditRules.IsTextTypeName(type));

    [Fact]
    public void IsTextColumn_AliasAndClrTypes()
    {
        Assert.True(MemoEditRules.IsTextColumn(Col("D_OBS", 200, baseType: "varchar")));
        Assert.False(MemoEditRules.IsTextColumn(new DataColumnInfo { TypeName = "geography", IsClrType = true }));
    }

    [Theory]
    [InlineData("varchar", 60, 60)]
    [InlineData("char", 1, 1)]
    [InlineData("nvarchar", 120, 60)]   // bytes → characters
    [InlineData("nchar", 20, 10)]
    [InlineData("nvarchar", -1, 0)]     // MAX: no limit
    [InlineData("varchar", -1, 0)]
    [InlineData("text", 16, 0)]
    [InlineData("ntext", 16, 0)]
    [InlineData("int", 4, 0)]
    public void MaxChars_SizedColumnsOnly(string type, int maxLengthBytes, int expected) =>
        Assert.Equal(expected, MemoEditRules.MaxChars(Col(type, maxLengthBytes)));

    [Fact]
    public void EditorText_NullOpensEmpty()
    {
        Assert.Equal("", MemoEditRules.EditorText(DBNull.Value));
        Assert.Equal("", MemoEditRules.EditorText(null));
        Assert.Equal("linha 1\r\nlinha 2", MemoEditRules.EditorText("linha 1\r\nlinha 2"));
    }

    [Fact]
    public void TryResolve_NullStaysNull_UnlessEdited()
    {
        Assert.False(MemoEditRules.TryResolve(DBNull.Value, "", edited: false, out _));
        Assert.False(MemoEditRules.TryResolve(DBNull.Value, "", edited: true, out _));   // typed and erased: still NULL
        Assert.True(MemoEditRules.TryResolve(DBNull.Value, "abc", edited: true, out var v));
        Assert.Equal("abc", v);
    }

    [Fact]
    public void TryResolve_ValueChanges_OnlyWhenTheTextDiffers()
    {
        Assert.False(MemoEditRules.TryResolve("abc", "abc", edited: true, out _));
        Assert.False(MemoEditRules.TryResolve("abc", "xyz", edited: false, out _));
        Assert.True(MemoEditRules.TryResolve("abc", "abc\r\ndef", edited: true, out var v));
        Assert.Equal("abc\r\ndef", v);
        Assert.True(MemoEditRules.TryResolve("abc", "", edited: true, out var empty)); // clearing a value: empty string (Ctrl+0 = NULL)
        Assert.Equal("", empty);
    }

    [Fact]
    public void CharCounter_Live()
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        Assert.Equal("0 chars", MemoEditRules.CharCounter("", pt));
        Assert.Equal("0 chars", MemoEditRules.CharCounter(null, pt));
        Assert.Equal("1 char", MemoEditRules.CharCounter("a", pt));
        Assert.Equal("5 chars", MemoEditRules.CharCounter("ação!", pt));
        Assert.Equal("4 chars", MemoEditRules.CharCounter("a\r\nb", pt));
        Assert.Equal("1.234 chars", MemoEditRules.CharCounter(new string('x', 1234), pt));
    }
}

/// <summary>Static audit of the WPF wiring of the "▾" calendar and the "..." memo window (source only, no GUI).</summary>
public class DateAndMemoWiringTests
{
    private static string Views()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SqlExpert.App", "Views");
    }

    private static string Src(string file) => File.ReadAllText(Path.Combine(Views(), file));

    [Fact]
    public void Grid_DateAndTextColumns_GetTheirSideButtons()
    {
        var grid = Src("TableDataGrid.xaml.cs");
        Assert.Contains("DateValueText.KindOf(spec.Info)", grid);
        Assert.Contains("new CellValueConverter(spec.DataType, dateKind)", grid);
        Assert.Contains("MemoEditRules.IsTextColumn(spec.Info)", grid);
        Assert.Contains("OpenGridCalendar", grid);
        Assert.Contains("OpenGridMemo", grid);
        Assert.Contains("new TableMemoSource(vm, spec, CommitEdit)", grid);
    }

    [Fact]
    public void Calendar_UsesPtBrLayout_TodayAndClear()
    {
        var cal = Src("DateCalendarPopup.cs");
        Assert.Contains("DateValueText.DayInitials()", cal);
        Assert.Contains("DateValueText.MonthDays(", cal);
        Assert.Contains("DateValueText.MonthTitle(", cal);
        Assert.Contains("\"Today\"", cal);
        Assert.Contains("\"Clear\"", cal);
        Assert.Contains("IsEnabled = canClear", cal);
        Assert.Contains("Key.Escape", cal);
    }

    [Fact]
    public void MemoWindow_HasNavigatorCounterAndButtons_AndFormReusesIt()
    {
        var memo = Src("MemoEditorWindow.cs");
        foreach (var part in new[] { "\"Plain text editor\"", "MemoEditRules.CharCounter(", "\"Copy to clipboard\"", "\"Close\"", "AcceptsReturn = true", "AcceptsTab = true", "\"Monospace\"", "MaxLength = Math.Max(0, source.MaxLength)", "source.IsReadOnly", "MemoEditRules.TryResolve(" })
            Assert.Contains(part, memo);
        var form = Src("TableDataGrid.Form.cs");
        Assert.Contains("OpenMemo(col, row)", form);
        Assert.Contains("new DateCalendarPopup(", form);
        Assert.DoesNotContain("MemoEditorWindow.Edit(", form);
        Assert.Contains("new ResultGridMemoSource(", Src("SqlEditorView.xaml.cs"));
    }
}
