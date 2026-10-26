using System.Globalization;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.DataForms;

/// <summary>SQL Server date/time column kinds with a calendar editor in the Data grid and Form View.</summary>
public enum DateColumnKind
{
    /// <summary>Not a date column (time, text, numbers, ...).</summary>
    None,
    /// <summary>date: yyyy-MM-dd.</summary>
    Date,
    /// <summary>smalldatetime: yyyy-MM-dd HH:mm:ss (minute precision, seconds always 00).</summary>
    SmallDateTime,
    /// <summary>datetime: yyyy-MM-dd HH:mm:ss.fff.</summary>
    DateTime,
    /// <summary>datetime2(n): yyyy-MM-dd HH:mm:ss plus n fractional digits.</summary>
    DateTime2,
    /// <summary>datetimeoffset(n): yyyy-MM-dd HH:mm:ss plus n fractional digits and +hh:mm.</summary>
    DateTimeOffset
}

/// <summary>
/// Text rules of date/time columns and the calendar pick rules (keep the time part, Today, Clear). Pure logic, no WPF.
/// Values are shown as the SQL Server Management Studio results grid shows them (ISO yyyy-MM-dd, fractional digits by
/// the column scale, see <see cref="Format(object?, DateColumnKind, int)"/>), the same text the grid copies to the
/// clipboard. Editors (grid cell, Form View, calendar) accept ONLY the pt-BR input mask of <see cref="DateEditMask"/>;
/// <see cref="Parse"/> (ISO or pt-BR text) is kept for pasting a copied cell value into a cell.
/// </summary>
public static class DateValueText
{
    /// <summary>Culture of the typed pt-BR dates and of the calendar (day initials d s t q q s s, month names).</summary>
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Fractional-second digits of datetime2 / time / datetimeoffset when the column scale is not known.</summary>
    public const int DefaultScale = 7;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static DateColumnKind KindOf(DataColumnInfo c) => KindOf(string.IsNullOrEmpty(c.BaseTypeName) ? c.TypeName : c.BaseTypeName);

    public static DateColumnKind KindOf(string? sqlTypeName)
    {
        var t = (sqlTypeName ?? "").Trim().ToLowerInvariant();
        var p = t.IndexOf('(');
        if (p >= 0) t = t[..p].Trim();
        return t switch
        {
            "date" => DateColumnKind.Date,
            "smalldatetime" => DateColumnKind.SmallDateTime,
            "datetime" => DateColumnKind.DateTime,
            "datetime2" => DateColumnKind.DateTime2,
            "datetimeoffset" => DateColumnKind.DateTimeOffset,
            _ => DateColumnKind.None
        };
    }

    public static bool HasTime(DateColumnKind kind) => kind is not (DateColumnKind.Date or DateColumnKind.None);

    /// <summary>
    /// Fractional-second digits shown for <paramref name="c"/>: the scale of a datetime2 / time / datetimeoffset column
    /// (0–7), <see cref="DefaultScale"/> when the column or its type is unknown.
    /// </summary>
    public static int ScaleOf(DataColumnInfo? c)
    {
        if (c is null) return DefaultScale;
        var t = BaseType(string.IsNullOrEmpty(c.BaseTypeName) ? c.TypeName : c.BaseTypeName);
        return t is "datetime2" or "time" or "datetimeoffset" && c.Scale is >= 0 and <= 7 ? c.Scale : DefaultScale;
    }

    /// <summary>Scale of a result column (SqlClient schema), as in <see cref="ScaleOf(DataColumnInfo?)"/>.</summary>
    public static int ScaleOf(string? sqlTypeName, int? scale) =>
        BaseType(sqlTypeName) is "datetime2" or "time" or "datetimeoffset" && scale is >= 0 and <= 7 ? scale.Value : DefaultScale;

    private static string BaseType(string? sqlTypeName)
    {
        var t = (sqlTypeName ?? "").Trim().ToLowerInvariant();
        var p = t.IndexOf('(');
        return p >= 0 ? t[..p].Trim() : t;
    }

    /// <summary>Text of a date column with the default scale (see <see cref="Format(object?, DateColumnKind, int)"/>).</summary>
    public static string Format(object? value, DateColumnKind kind) => Format(value, kind, DefaultScale);

    /// <summary>
    /// Grid / editor / form / clipboard text, as the SQL Server Management Studio results grid shows it:
    /// date yyyy-MM-dd; smalldatetime yyyy-MM-dd HH:mm:ss; datetime yyyy-MM-dd HH:mm:ss.fff; datetime2(n) and time(n)
    /// with n = <paramref name="scale"/> fractional digits; datetimeoffset(n) adds " +hh:mm". Values of unknown columns
    /// (<see cref="DateColumnKind.None"/>) follow <see cref="FormatUnknown"/>. NULL → "".
    /// </summary>
    public static string Format(object? value, DateColumnKind kind, int scale)
    {
        switch (value)
        {
            case null or DBNull:
                return "";
            case System.DateTimeOffset o:
                return o.ToString("yyyy-MM-dd HH:mm:ss", Inv) + Fraction(o.Ticks, scale) + " " + o.ToString("zzz", Inv);
            case System.DateTime d:
                return kind switch
                {
                    DateColumnKind.Date => d.ToString("yyyy-MM-dd", Inv),
                    DateColumnKind.SmallDateTime => d.ToString("yyyy-MM-dd HH:mm:ss", Inv),
                    DateColumnKind.DateTime => d.ToString("yyyy-MM-dd HH:mm:ss.fff", Inv),
                    DateColumnKind.DateTime2 or DateColumnKind.DateTimeOffset => d.ToString("yyyy-MM-dd HH:mm:ss", Inv) + Fraction(d.Ticks, scale),
                    _ => FormatUnknown(d)
                };
            case TimeSpan t:
                return FormatTime(t, scale);
            default:
                return Convert.ToString(value, Inv) ?? "";
        }
    }

    /// <summary>
    /// Date without column type (e.g. a value shown with no metadata): yyyy-MM-dd at midnight, otherwise as datetime
    /// (yyyy-MM-dd HH:mm:ss.fff), with 7 fractional digits when the value is finer than a millisecond.
    /// </summary>
    public static string FormatUnknown(System.DateTime d)
    {
        if (d.TimeOfDay == TimeSpan.Zero) return d.ToString("yyyy-MM-dd", Inv);
        return d.Ticks % TimeSpan.TicksPerMillisecond == 0
            ? d.ToString("yyyy-MM-dd HH:mm:ss.fff", Inv)
            : d.ToString("yyyy-MM-dd HH:mm:ss.fffffff", Inv);
    }

    /// <summary>time(n): HH:mm:ss plus <paramref name="scale"/> fractional digits.</summary>
    public static string FormatTime(TimeSpan t, int scale = DefaultScale) =>
        (t < TimeSpan.Zero ? "-" : "") + ((int)Math.Abs(t.TotalHours)).ToString("00", Inv)
        + t.ToString(@"\:mm\:ss", Inv) + Fraction(t.Ticks, scale);

    /// <summary>"." plus <paramref name="digits"/> (0–7) fractional-second digits (truncated); empty when 0.</summary>
    private static string Fraction(long ticks, int digits)
    {
        digits = Math.Clamp(digits, 0, 7);
        if (digits == 0) return "";
        return "." + Math.Abs(ticks % TimeSpan.TicksPerSecond).ToString("0000000", Inv)[..digits];
    }

    private static readonly string[] DateFormats = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" };

    private static readonly string[] TimeFormats = { "HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss", "HH:mm:ss.FFFFFFF", "H:mm:ss.FFFFFFF" };

    private static readonly string[] DateTimeFormats = BuildDateTimeFormats();

    private static string[] BuildDateTimeFormats()
    {
        var list = new List<string>(DateFormats);
        foreach (var d in DateFormats)
            foreach (var t in TimeFormats)
            {
                list.Add(d + " " + t);
                if (d.StartsWith("yyyy", StringComparison.Ordinal)) list.Add(d + "'T'" + t);
            }
        return list.ToArray();
    }

    private static readonly string[] OffsetFormats = DateTimeFormats.SelectMany(f => new[] { f + " zzz", f + "zzz", f }).ToArray();

    /// <summary>
    /// Parses a pasted / copied date (grid "Paste cell value"; editors use <see cref="DateEditMask.Parse"/>): ISO yyyy-MM-dd (the displayed format) or pt-BR dd/MM/yyyy (also d/M/yyyy), followed by HH:mm[:ss[.fffffff]] where the
    /// type has a time part (ISO also with "T"); datetimeoffset may end in +hh:mm (default: the local offset).
    /// Blank → NULL. Throws <see cref="FormatException"/> on anything else (the grid shows a validation error).
    /// </summary>
    public static object Parse(string? text, DateColumnKind kind, Type clrType)
    {
        if (string.IsNullOrWhiteSpace(text)) return DBNull.Value;
        var s = text.Trim();
        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces;
        if (kind == DateColumnKind.DateTimeOffset || clrType == typeof(System.DateTimeOffset))
        {
            if (System.DateTimeOffset.TryParseExact(s, OffsetFormats, PtBr, styles | DateTimeStyles.AssumeLocal, out var o)) return o;
            throw new FormatException($"Data inválida: \"{s}\". Use dd/MM/aaaa HH:mm:ss [+hh:mm] ou aaaa-MM-dd.");
        }

        var formats = kind == DateColumnKind.Date ? DateFormats : DateTimeFormats;
        if (!System.DateTime.TryParseExact(s, formats, PtBr, styles, out var d))
            throw new FormatException(kind == DateColumnKind.Date
                ? $"Data inválida: \"{s}\". Use dd/MM/aaaa ou aaaa-MM-dd."
                : $"Data inválida: \"{s}\". Use dd/MM/aaaa HH:mm:ss ou aaaa-MM-dd HH:mm:ss.");
        if (kind == DateColumnKind.SmallDateTime && (d.Year < 1900 || d.Year > 2079))
            throw new FormatException("smalldatetime aceita datas de 01/01/1900 a 06/06/2079.");
        if (kind == DateColumnKind.DateTime && d.Year < 1753)
            throw new FormatException("datetime aceita datas a partir de 01/01/1753.");
        return d;
    }

    /// <summary>
    /// Day clicked in the calendar: the date part becomes <paramref name="picked"/>; datetime-style types keep the
    /// current time part (and offset); a NULL value gets 00:00 (and the local offset).
    /// </summary>
    public static object PickDay(object? current, System.DateTime picked, DateColumnKind kind)
    {
        var date = picked.Date;
        if (kind == DateColumnKind.DateTimeOffset || current is System.DateTimeOffset)
        {
            if (current is System.DateTimeOffset o) return new System.DateTimeOffset(date + o.TimeOfDay, o.Offset);
            return new System.DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
        }
        if (kind == DateColumnKind.Date) return date;
        return current is System.DateTime d ? date + d.TimeOfDay : date;
    }

    /// <summary>[Today]: today's date, keeping the time part when the value has one (00:00 otherwise).</summary>
    public static object Today(object? current, DateColumnKind kind, System.DateTime today) => PickDay(current, today, kind);

    /// <summary>[Clear] sets NULL: only for nullable, editable columns (the button is disabled otherwise).</summary>
    public static bool CanClear(bool isNullable, bool isReadOnly) => isNullable && !isReadOnly;

    /// <summary>Date shown as selected in the calendar (null for NULL / not a date).</summary>
    public static System.DateTime? SelectedDate(object? value) => value switch
    {
        System.DateTime d => d.Date,
        System.DateTimeOffset o => o.Date,
        _ => null
    };

    // ===== calendar layout =====

    /// <summary>Day-of-week initials from Sunday, lower case, as in the pt-BR calendar: d s t q q s s.</summary>
    public static IReadOnlyList<string> DayInitials(CultureInfo? culture = null)
    {
        var c = culture ?? PtBr;
        var first = (int)c.DateTimeFormat.FirstDayOfWeek;
        return Enumerable.Range(0, 7)
            .Select(i => c.DateTimeFormat.DayNames[(first + i) % 7])
            .Select(n => n.Length == 0 ? "" : char.ToLower(n[0], c).ToString())
            .ToList();
    }

    /// <summary>Header of the calendar: "abril 2011".</summary>
    public static string MonthTitle(int year, int month, CultureInfo? culture = null)
    {
        var c = culture ?? PtBr;
        var name = c.DateTimeFormat.GetMonthName(month);
        return $"{name} {year}";
    }

    /// <summary>
    /// The 42 days (6 weeks) shown for a month: starts on the culture's first day of the week on or before the 1st,
    /// so the last days of the previous month and the first days of the next one fill the grid (shown greyed).
    /// </summary>
    public static IReadOnlyList<System.DateTime> MonthDays(int year, int month, CultureInfo? culture = null)
    {
        var c = culture ?? PtBr;
        var first = new System.DateTime(year, month, 1);
        var offset = ((int)first.DayOfWeek - (int)c.DateTimeFormat.FirstDayOfWeek + 7) % 7;
        var start = first.AddDays(-offset);
        return Enumerable.Range(0, 42).Select(i => start.AddDays(i)).ToList();
    }
}
