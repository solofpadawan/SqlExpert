using System.Globalization;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.DataForms;

/// <summary>Value editor used by the Form View for one column.</summary>
public enum FormFieldKind
{
    /// <summary>Single-line text box.</summary>
    Text,
    /// <summary>Text box + "..." button (multiline memo editor): MAX / text / ntext / xml / long columns.</summary>
    Memo,
    /// <summary>Right-aligned numeric text box.</summary>
    Number,
    /// <summary>date: text + calendar.</summary>
    Date,
    /// <summary>datetime / datetime2 / smalldatetime / datetimeoffset: text + calendar (keeps the time part).</summary>
    DateTime,
    /// <summary>time: text.</summary>
    Time,
    /// <summary>bit: check box.</summary>
    Boolean,
    /// <summary>binary / varbinary / image / rowversion: read-only "&lt;binary n bytes&gt; 0x…".</summary>
    Binary,
    /// <summary>CLR types (geography, hierarchyid, …) and sql_variant: read-only text.</summary>
    ReadOnlyText
}

/// <summary>One row of the Form View (name | type | null | value).</summary>
public sealed record FormFieldSpec(
    string Field,
    string Name,
    string TypeText,
    FormFieldKind Kind,
    Type ClrType,
    bool IsReadOnly,
    bool IsNullable,
    bool IsPrimaryKey,
    bool IsBold,
    string ToolTip)
{
    /// <summary>The "null" checkbox can be toggled.</summary>
    public bool CanToggleNull => !IsReadOnly && IsNullable;
}

/// <summary>Pure rules behind the Form View and Print Data (no WPF).</summary>
public static class FormFieldLayout
{
    /// <summary>Declared character length above which a string column is edited/printed as a memo.</summary>
    public const int MemoLength = 255;

    public static bool IsNumericType(Type t) =>
        t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
        || t == typeof(decimal) || t == typeof(double) || t == typeof(float);

    public static bool IsIntegerType(Type t) =>
        t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte);

    private static string Base(DataColumnInfo c) =>
        (string.IsNullOrEmpty(c.BaseTypeName) ? c.TypeName : c.BaseTypeName).ToLowerInvariant();

    /// <summary>Declared length in characters (nchar/nvarchar: bytes / 2); -1 = MAX; 0 = not applicable/unknown.</summary>
    public static int CharLength(DataColumnInfo c)
    {
        if (c.MaxLength == -1) return -1;
        if (c.MaxLength <= 0) return 0;
        return Base(c) is "nchar" or "nvarchar" ? c.MaxLength / 2 : c.MaxLength;
    }

    /// <summary>True for MAX, text, ntext, xml and string columns longer than <see cref="MemoLength"/>.</summary>
    public static bool IsMemoColumn(DataColumnInfo c)
    {
        var b = Base(c);
        if (b is "text" or "ntext" or "xml") return true;
        if (b is not ("char" or "varchar" or "nchar" or "nvarchar")) return false;
        var len = CharLength(c);
        return len == -1 || len > MemoLength;
    }

    /// <summary>"INT", "NVARCHAR(60)", "VARCHAR(MAX)", "NUMERIC(15,2)", "DATETIME2(3)", "D_NOME (NVARCHAR(60))".</summary>
    public static string TypeText(DataColumnInfo c)
    {
        var b = Base(c);
        var baseText = b.ToUpperInvariant();
        switch (b)
        {
            case "char" or "varchar" or "nchar" or "nvarchar" or "binary" or "varbinary":
            {
                var len = b is "binary" or "varbinary" ? (c.MaxLength == -1 ? -1 : c.MaxLength) : CharLength(c);
                if (len == -1) baseText += "(MAX)";
                else if (len > 0) baseText += "(" + len.ToString(CultureInfo.InvariantCulture) + ")";
                break;
            }
            case "decimal" or "numeric" when c.Precision > 0:
                baseText += $"({c.Precision.ToString(CultureInfo.InvariantCulture)},{c.Scale.ToString(CultureInfo.InvariantCulture)})";
                break;
            case "datetime2" or "time" or "datetimeoffset" when c.MaxLength > 0 && c.Scale != 7:
                baseText += "(" + c.Scale.ToString(CultureInfo.InvariantCulture) + ")";
                break;
        }

        if (string.IsNullOrEmpty(c.TypeName) || string.Equals(c.TypeName, b, StringComparison.OrdinalIgnoreCase))
            return baseText;
        return $"{c.TypeName.ToUpperInvariant()} ({baseText})";
    }

    public static FormFieldKind KindOf(DataColumnInfo c, Type clrType)
    {
        if (c.IsClrType || clrType == typeof(object)) return FormFieldKind.ReadOnlyText;
        if (clrType == typeof(byte[])) return FormFieldKind.Binary;
        if (clrType == typeof(bool)) return FormFieldKind.Boolean;
        if (IsNumericType(clrType)) return FormFieldKind.Number;
        if (clrType == typeof(TimeSpan)) return FormFieldKind.Time;
        if (clrType == typeof(DateTimeOffset)) return FormFieldKind.DateTime;
        if (clrType == typeof(DateTime)) return Base(c) == "date" ? FormFieldKind.Date : FormFieldKind.DateTime;
        if (clrType == typeof(string)) return IsMemoColumn(c) ? FormFieldKind.Memo : FormFieldKind.Text;
        return FormFieldKind.Text;
    }

    /// <summary>
    /// Builds the form rows with the same editing rules as the grid: <paramref name="columnReadOnly"/> is the grid's per-column
    /// rule (identity, computed, rowversion, CLR, binary), <paramref name="tableReadOnly"/> is "no primary key".
    /// </summary>
    public static FormFieldSpec Build(string field, DataColumnInfo c, Type clrType, bool columnReadOnly, bool tableReadOnly)
    {
        var kind = KindOf(c, clrType);
        var readOnly = columnReadOnly || tableReadOnly || kind is FormFieldKind.Binary or FormFieldKind.ReadOnlyText;
        var type = TypeText(c);
        var notes = new List<string>();
        if (c.IsPrimaryKey) notes.Add("chave primária");
        if (!c.IsNullable) notes.Add("NOT NULL");
        if (c.IsIdentity) notes.Add("identity");
        if (c.IsComputed) notes.Add("computed");
        if (c.IsRowVersion) notes.Add("rowversion");
        if (readOnly) notes.Add("somente leitura");
        var tip = notes.Count == 0 ? $"{c.Name}: {type}" : $"{c.Name}: {type} — {string.Join(", ", notes)}";
        return new FormFieldSpec(field, c.Name, type, kind, clrType, readOnly, c.IsNullable, c.IsPrimaryKey,
            c.IsPrimaryKey || !c.IsNullable, tip);
    }

    /// <summary>Value set when the "null" checkbox is cleared: "" / 0 / false / today / Guid.Empty.</summary>
    public static object DefaultValueFor(Type t)
    {
        if (t == typeof(string)) return "";
        if (t == typeof(bool)) return false;
        if (t == typeof(DateTime)) return DateTime.Today;
        if (t == typeof(DateTimeOffset)) return new DateTimeOffset(DateTime.Today);
        if (t == typeof(TimeSpan)) return TimeSpan.Zero;
        if (t == typeof(Guid)) return Guid.Empty;
        if (IsNumericType(t)) return Convert.ChangeType(0, t, CultureInfo.InvariantCulture);
        return DBNull.Value;
    }

    /// <summary>Calendar pick: replaces the date part and keeps the time (and offset) of the current value.</summary>
    public static object ReplaceDate(object? current, DateTime picked, Type t)
    {
        var date = picked.Date;
        if (t == typeof(DateTimeOffset))
        {
            var cur = current is DateTimeOffset o ? o : new DateTimeOffset(date);
            return new DateTimeOffset(date + cur.TimeOfDay, cur.Offset);
        }

        return current is DateTime d ? date + d.TimeOfDay : date;
    }

    /// <summary>
    /// Typing filter of the numeric box: digits, one leading sign, one decimal separator (non-integer types),
    /// exponent for float/double. Whole-text check of the text as it would be after the keystroke.
    /// </summary>
    public static bool IsAcceptableNumericInput(string proposed, Type t, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(proposed)) return true;
        var dec = culture.NumberFormat.NumberDecimalSeparator;
        var group = culture.NumberFormat.NumberGroupSeparator;
        var allowDecimal = !IsIntegerType(t);
        var allowExp = t == typeof(double) || t == typeof(float);
        var allowSign = t != typeof(byte);
        var seenDec = false;
        var seenExp = false;
        for (var i = 0; i < proposed.Length; i++)
        {
            var ch = proposed[i];
            if (char.IsDigit(ch)) continue;
            if ((ch == '-' || ch == '+') && allowSign && (i == 0 || (seenExp && (proposed[i - 1] is 'e' or 'E')))) continue;
            if (allowDecimal && !seenDec && !seenExp && string.CompareOrdinal(proposed, i, dec, 0, dec.Length) == 0)
            {
                seenDec = true;
                i += dec.Length - 1;
                continue;
            }
            if (!seenDec && group.Length > 0 && string.CompareOrdinal(proposed, i, group, 0, group.Length) == 0 && group != dec)
            {
                i += group.Length - 1;
                continue;
            }
            if (allowExp && !seenExp && (ch is 'e' or 'E') && i > 0) { seenExp = true; continue; }
            return false;
        }

        return true;
    }
}
