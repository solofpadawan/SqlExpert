using System.Globalization;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.DataForms;

/// <summary>SQL Server date/time column kinds with a calendar editor in the Data grid and Form View.</summary>
public enum DateColumnKind
{
    /// <summary>Not a date column (time, text, numbers, ...).</summary>
    None,
    /// <summary>date: dd/MM/yyyy.</summary>
    Date,
    /// <summary>smalldatetime: dd/MM/yyyy HH:mm (minute precision).</summary>
    SmallDateTime,
    /// <summary>datetime: dd/MM/yyyy HH:mm:ss[.fff].</summary>
    DateTime,
    /// <summary>datetime2: dd/MM/yyyy HH:mm:ss[.fffffff] (full precision kept).</summary>
    DateTime2,
    /// <summary>datetimeoffset: dd/MM/yyyy HH:mm:ss[.fffffff] +hh:mm.</summary>
    DateTimeOffset
}

/// <summary>
/// pt-BR text rules of date columns (dd/MM/yyyy, plus the time where the type has one) and the calendar pick rules
/// (keep the time part, Today, Clear). Pure logic, no WPF.
/// </summary>
public static class DateValueText
{
    /// <summary>Culture of the date texts and of the calendar (day initials d s t q q s s, month names).</summary>
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private const string DateFormat = "dd/MM/yyyy";
    private const string FractionFormat = "dd/MM/yyyy HH:mm:ss.FFFFFFF";

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
    /// Grid / editor / form text: "dd/MM/yyyy" (date), "dd/MM/yyyy HH:mm" (smalldatetime), "dd/MM/yyyy HH:mm:ss" plus
    /// the fraction when there is one (datetime, datetime2), and the offset for datetimeoffset. NULL → "".
    /// </summary>
    public static string Format(object? value, DateColumnKind kind)
    {
        switch (value)
        {
            case null or DBNull:
                return "";
            case System.DateTimeOffset o:
                return o.ToString(FractionFormat, PtBr) + " " + o.ToString("zzz", PtBr);
            case System.DateTime d:
                return kind switch
                {
                    DateColumnKind.Date => d.ToString(DateFormat, PtBr),
                    DateColumnKind.SmallDateTime => d.ToString("dd/MM/yyyy HH:mm", PtBr),
                    _ => d.ToString(FractionFormat, PtBr)
                };
            default:
                return Convert.ToString(value, PtBr) ?? "";
        }
    }

    private static readonly string[] DateFormats = { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" };

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
    /// Parses a typed date: pt-BR dd/MM/yyyy (also d/M/yyyy) or ISO yyyy-MM-dd, followed by HH:mm[:ss[.fffffff]] where the
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
