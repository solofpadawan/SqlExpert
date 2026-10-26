using System.Globalization;
using System.Text;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.Core.DataForms;

/// <summary>One record copied from the Data grid: values in column order, plus the original values (pending edits).</summary>
/// <param name="Values">Current values, aligned with the column list (null / DBNull = NULL).</param>
/// <param name="OriginalValues">Values read from the database (rows with pending edits); used in UPDATE ... WHERE.
/// Null = same as <paramref name="Values"/>.</param>
public sealed record CopyRecord(IReadOnlyList<object?> Values, IReadOnlyList<object?>? OriginalValues = null);

/// <summary>
/// Data grid context menu "Copy records / Copy selected record(s) [as INSERT / as UPDATE]".
/// Tab-separated text (Excel-friendly) and T-SQL scripts with literal values.
/// <para>
/// INSERT rules: computed and rowversion columns are left out (the server fills them). Identity columns are kept, so
/// the script reproduces the records exactly (copying rows between databases is the usual use), and the script is
/// wrapped in SET IDENTITY_INSERT ... ON / OFF.
/// </para>
/// <para>
/// UPDATE rules: WHERE uses the primary key (original values when the row has pending edits); the SET list has every
/// other writable column (no key, identity, computed or rowversion columns). Without a primary key the WHERE lists
/// every comparable column (no binary, text/ntext/image, xml or spatial columns).
/// </para>
/// </summary>
public static class RecordCopyText
{
    // ===== tab-separated text =====

    /// <summary>
    /// "Copy records / Copy selected record(s) to clipboard" text: header line (column names) plus one line per
    /// record, TAB between fields, CRLF between lines and no line break after the last record. Fields are never
    /// quoted: TAB / CR / LF inside a value become a single space (see <see cref="TabField"/>), so every record stays
    /// on one line and pastes into one spreadsheet row. Field values come from <see cref="RecordFieldText"/>.
    /// </summary>
    public static string ToTabSeparated(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        var sb = new StringBuilder();
        AppendLine(sb, headers);
        foreach (var row in rows)
        {
            sb.Append("\r\n");
            AppendLine(sb, row);
        }
        return sb.ToString();
    }

    private static void AppendLine(StringBuilder sb, IReadOnlyList<string?> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0) sb.Append('\t');
            sb.Append(TabField(fields[i]));
        }
    }

    /// <summary>One TSV field, as is (no quotes): each TAB, CR, LF or CRLF becomes one space; null → empty.</summary>
    public static string TabField(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.IndexOfAny(new[] { '\t', '\r', '\n' }) < 0) return text;
        return text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    }

    /// <summary>
    /// Value text of one TSV field (Copy records), as SQL Server Management Studio's "Copy with Headers" writes it,
    /// independent of the regional settings: NULL → "NULL"; integers without thousand separators; decimal / numeric /
    /// money / float / real with "." as decimal separator and no grouping (decimal keeps its scale: money 10.5000);
    /// bit → 1 / 0; dates and times as the grid shows them (<see cref="DateValueText.Format(object?, DateColumnKind, int)"/>:
    /// date yyyy-MM-dd; smalldatetime yyyy-MM-dd HH:mm:ss; datetime yyyy-MM-dd HH:mm:ss.fff; datetime2(n) / time(n)
    /// with n fractional digits; datetimeoffset(n) adds " +hh:mm");
    /// uniqueidentifier in upper case; binary → full hex (0x…); text as is.
    /// </summary>
    public static string RecordFieldText(object? value, DataColumnInfo? column)
    {
        if (DataValueText.IsNull(value)) return "NULL";
        var inv = CultureInfo.InvariantCulture;
        switch (value)
        {
            case string s:
                return s;
            case byte[] b:
                return DataValueText.ToHex(b, int.MaxValue);
            case bool flag:
                return flag ? "1" : "0";
            case DateTime or DateTimeOffset or TimeSpan: // same text as the grid shows
                return DateValueText.Format(value, column is null ? DateColumnKind.None : DateValueText.KindOf(column), DateValueText.ScaleOf(column));
            case Guid g:
                return g.ToString("D", inv).ToUpperInvariant();
            case double x:
                return x.ToString("R", inv);
            case float f:
                return f.ToString("R", inv);
            case IFormattable other: // integers, decimal (scale kept), ...
                return other.ToString(null, inv);
            default:
                return value!.ToString() ?? "";
        }
    }

    /// <summary>"Copy cell value" text, as the grid shows it (pastes back into a grid cell): NULL → empty, binary → full hex.</summary>
    public static string ValueText(object? value, DataColumnInfo? column, CultureInfo culture)
    {
        if (DataValueText.IsNull(value)) return "";
        if (value is byte[] b) return DataValueText.ToHex(b, int.MaxValue);
        if (value is DateTime or DateTimeOffset or TimeSpan)
            return DateValueText.Format(value, column is null ? DateColumnKind.None : DateValueText.KindOf(column), DateValueText.ScaleOf(column));
        return DataValueText.Format(value, culture, int.MaxValue);
    }

    // ===== T-SQL literals =====

    /// <summary>
    /// T-SQL literal of <paramref name="value"/>: NULL, text quoted with doubled quotes (N'…' only for Unicode
    /// columns or when the column type is unknown, plain '…' otherwise; see <see cref="UsesUnicodeLiteral"/>), numbers in invariant culture,
    /// bit 0/1, 0x… binary, quoted uniqueidentifier, ISO 8601 dates ('yyyy-MM-ddTHH:mm:ss.fff'; datetime2 keeps 7
    /// fraction digits, date keeps only the day, datetimeoffset keeps the offset).
    /// </summary>
    public static string Literal(object? value, DataColumnInfo? column = null)
    {
        var inv = CultureInfo.InvariantCulture;
        switch (value)
        {
            case null or DBNull:
                return "NULL";
            case string s:
                return TextLiteral(s, column);
            case char c:
                return TextLiteral(c.ToString(), column);
            case bool b:
                return b ? "1" : "0";
            case byte[] bytes:
                return DataValueText.ToHex(bytes, int.MaxValue);
            case Guid g:
                return "'" + g.ToString("D", inv) + "'";
            case DateTime d:
                return "'" + d.ToString(DateTimePattern(column), inv) + "'";
            case DateTimeOffset o:
                return "'" + o.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", inv) + "'";
            case TimeSpan t:
                return "'" + t.ToString(@"hh\:mm\:ss\.fffffff", inv) + "'";
            case float f:
                return float.IsFinite(f) ? f.ToString("R", inv) : "NULL";
            case double dbl:
                return double.IsFinite(dbl) ? dbl.ToString("R", inv) : "NULL";
            case decimal m:
                return m.ToString(inv);
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return ((IFormattable)value).ToString(null, inv);
            case IFormattable other:
                return TextLiteral(other.ToString(null, inv), column);
            default:
                return TextLiteral(value.ToString() ?? "", column);
        }
    }

    /// <summary>
    /// Whether text for <paramref name="column"/> is written as N'…': nvarchar / nchar / ntext / sysname, xml and
    /// sql_variant (may hold Unicode), and any column whose type is unknown (safe default). varchar / char / text and
    /// the other types written from text (dates, guid, hierarchyid, …) get a plain '…' literal, as in SSMS scripts.
    /// </summary>
    public static bool UsesUnicodeLiteral(DataColumnInfo? column)
    {
        var type = TypeOf(column);
        if (type.Length == 0) return true;
        var paren = type.IndexOf('(');
        if (paren >= 0) type = type[..paren].Trim();
        return type is "nvarchar" or "nchar" or "ntext" or "sysname" or "xml" or "sql_variant";
    }

    private static string TypeOf(DataColumnInfo? column)
    {
        if (column is null) return "";
        var baseType = (column.BaseTypeName ?? "").Trim();
        var declared = (column.TypeName ?? "").Trim();
        return (baseType.Length > 0 ? baseType : declared).ToLowerInvariant();
    }

    private static string TextLiteral(string text, DataColumnInfo? column) =>
        UsesUnicodeLiteral(column) ? SqlText.NLiteral(text) : "'" + text.Replace("'", "''") + "'";

    private static string DateTimePattern(DataColumnInfo? column)
    {
        var type = column is null ? "" : (string.IsNullOrEmpty(column.BaseTypeName) ? column.TypeName : column.BaseTypeName).Trim().ToLowerInvariant();
        return type switch
        {
            "date" => "yyyy-MM-dd",
            "datetime2" => "yyyy-MM-dd'T'HH:mm:ss.fffffff",
            _ => "yyyy-MM-dd'T'HH:mm:ss.fff"
        };
    }

    // ===== INSERT =====

    /// <summary>Columns written by the INSERT script: every column except computed and rowversion columns.</summary>
    public static bool IsInsertColumn(DataColumnInfo c) => !c.IsComputed && !c.IsRowVersion;

    /// <summary>Columns of the UPDATE SET list: writable columns that are not part of the primary key.</summary>
    public static bool IsSetColumn(DataColumnInfo c, bool usePrimaryKey) =>
        !c.IsIdentity && !c.IsComputed && !c.IsRowVersion && !(usePrimaryKey && c.IsPrimaryKey);

    /// <summary>
    /// One "INSERT INTO [schema].[table] ([c1], ...) VALUES (...);" line per record. When an identity column is
    /// written, the lines are wrapped in SET IDENTITY_INSERT [schema].[table] ON; / OFF;.
    /// </summary>
    public static string BuildInserts(string schema, string table, IReadOnlyList<DataColumnInfo> columns, IEnumerable<CopyRecord> records)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(records);
        var target = SqlText.Qualified(schema, table);
        var used = Enumerable.Range(0, columns.Count).Where(i => IsInsertColumn(columns[i])).ToList();
        var list = string.Join(", ", used.Select(i => SqlText.Bracket(columns[i].Name)));
        var identity = used.Any(i => columns[i].IsIdentity);

        var sb = new StringBuilder();
        if (identity) sb.Append("SET IDENTITY_INSERT ").Append(target).Append(" ON;\r\n");
        foreach (var r in records)
        {
            sb.Append("INSERT INTO ").Append(target).Append(" (").Append(list).Append(") VALUES (");
            sb.Append(string.Join(", ", used.Select(i => Literal(ValueAt(r.Values, i), columns[i]))));
            sb.Append(");\r\n");
        }
        if (identity) sb.Append("SET IDENTITY_INSERT ").Append(target).Append(" OFF;\r\n");
        return sb.ToString();
    }

    // ===== UPDATE =====

    /// <summary>True for columns that can be compared in a WHERE (no binary, LOB, xml or spatial types).</summary>
    public static bool IsComparable(DataColumnInfo c)
    {
        var type = (string.IsNullOrEmpty(c.BaseTypeName) ? c.TypeName : c.BaseTypeName).Trim().ToLowerInvariant();
        if (type is "text" or "ntext" or "image" or "xml" or "geography" or "geometry" or "binary" or "varbinary"
            or "timestamp" or "rowversion" or "sql_variant")
            return false;
        return !c.IsClrType || type == "hierarchyid";
    }

    /// <summary>
    /// One "UPDATE [schema].[table] SET [c] = v, ... WHERE [pk] = v;" line per record. Without a primary key the WHERE
    /// compares every comparable column (NULL as IS NULL). A record whose every column is in the key gives a comment.
    /// </summary>
    public static string BuildUpdates(string schema, string table, IReadOnlyList<DataColumnInfo> columns, IEnumerable<CopyRecord> records)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(records);
        var target = SqlText.Qualified(schema, table);
        var usePk = columns.Any(c => c.IsPrimaryKey);
        var whereCols = usePk
            ? Enumerable.Range(0, columns.Count).Where(i => columns[i].IsPrimaryKey).OrderBy(i => columns[i].KeyOrdinal).ToList()
            : Enumerable.Range(0, columns.Count).Where(i => IsComparable(columns[i])).ToList();
        var setCols = Enumerable.Range(0, columns.Count).Where(i => IsSetColumn(columns[i], usePk)).ToList();

        var sb = new StringBuilder();
        if (!usePk)
            sb.Append("-- ").Append(target).Append(": no primary key, WHERE compares every column.\r\n");
        foreach (var r in records)
        {
            if (setCols.Count == 0 || whereCols.Count == 0)
            {
                sb.Append("-- ").Append(target).Append(setCols.Count == 0
                    ? ": nothing to update (every column is part of the key or read-only).\r\n"
                    : ": no column to identify the record.\r\n");
                continue;
            }
            sb.Append("UPDATE ").Append(target).Append(" SET ");
            sb.Append(string.Join(", ", setCols.Select(i => SqlText.Bracket(columns[i].Name) + " = " + Literal(ValueAt(r.Values, i), columns[i]))));
            sb.Append(" WHERE ");
            var keySource = r.OriginalValues ?? r.Values;
            sb.Append(string.Join(" AND ", whereCols.Select(i =>
            {
                var v = ValueAt(keySource, i);
                return SqlText.Bracket(columns[i].Name) + (DataValueText.IsNull(v) ? " IS NULL" : " = " + Literal(v, columns[i]));
            })));
            sb.Append(";\r\n");
        }
        return sb.ToString();
    }

    private static object? ValueAt(IReadOnlyList<object?> values, int i) => i < values.Count ? values[i] : null;

    // ===== Set as NOW =====

    /// <summary>
    /// "Set as NOW" value for a column: date → today; smalldatetime → minute precision; datetime → millisecond;
    /// datetime2 → as is; datetimeoffset → local time with offset; time → time of day. Null when the column is not a
    /// date/time column.
    /// </summary>
    public static object? NowValue(DataColumnInfo column, Type dataType, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(column);
        var local = now.DateTime;
        if (dataType == typeof(TimeSpan)) return local.TimeOfDay;
        var kind = DateValueText.KindOf(column);
        if (dataType == typeof(DateTimeOffset)) return kind is DateColumnKind.DateTimeOffset or DateColumnKind.None ? now : null;
        if (dataType != typeof(DateTime)) return null;
        return kind switch
        {
            DateColumnKind.Date => local.Date,
            DateColumnKind.SmallDateTime => new DateTime(local.Ticks - local.Ticks % TimeSpan.TicksPerMinute, local.Kind),
            DateColumnKind.DateTime2 => local,
            _ => new DateTime(local.Ticks - local.Ticks % TimeSpan.TicksPerMillisecond, local.Kind)
        };
    }
}
