using System.Globalization;
using System.Text;

namespace SqlExpert.Core.DataForms;

/// <summary>
/// Text ⇄ value rules shared by the Data grid, the Form View and Print Data.
/// NULL is "&lt;null&gt;" in the grid, empty in the form (the form has its own "null" checkbox) and empty in print.
/// </summary>
public static class DataValueText
{
    public const string NullText = "<null>";
    public const string MemoPlaceholder = "(MEMO)";
    public const string BlobPlaceholder = "(BLOB)";
    /// <summary>Strings longer than this are treated as memo values when printing.</summary>
    public const int MemoThreshold = 255;
    /// <summary>Longest text kept for one printed cell (it is trimmed to the column width anyway).</summary>
    public const int MaxPrintChars = 400;

    public static bool IsNull(object? value) => value is null || value is DBNull;

    /// <summary>Grid/form display text (current culture rules), without the NULL marker.</summary>
    public static string Format(object? value, CultureInfo culture, int maxHexBytes = 32) => value switch
    {
        null or DBNull => "",
        byte[] b => ToHex(b, maxHexBytes),
        DateTime d when d.TimeOfDay == TimeSpan.Zero => d.ToString("d", culture),
        DateTime d when d.Ticks % TimeSpan.TicksPerSecond != 0 => d.ToString(culture.DateTimeFormat.ShortDatePattern + " HH:mm:ss.FFFFFFF", culture),
        IFormattable f => f.ToString(null, culture),
        _ => value.ToString() ?? ""
    };

    /// <summary>Grid cell text: like <see cref="Format"/> but NULL is "&lt;null&gt;".</summary>
    public static string FormatCell(object? value, CultureInfo culture) => IsNull(value) ? NullText : Format(value, culture);

    /// <summary>"0x0102…" (first <paramref name="maxBytes"/> bytes, "..." when longer).</summary>
    public static string ToHex(byte[] bytes, int maxBytes = 32)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        maxBytes = Math.Max(0, maxBytes);
        var n = Math.Min(bytes.Length, maxBytes);
        var sb = new StringBuilder("0x", 2 + n * 2 + 3);
        for (var i = 0; i < n; i++)
            sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        if (bytes.Length > maxBytes) sb.Append("...");
        return sb.ToString();
    }

    /// <summary>Form View text of a binary value: "&lt;binary 1.234 bytes&gt; 0x…".</summary>
    public static string BinarySummary(object? value, CultureInfo culture)
    {
        if (value is not byte[] b) return IsNull(value) ? "" : value!.ToString() ?? "";
        return $"<binary {b.Length.ToString("N0", culture)} bytes> {ToHex(b, 16)}";
    }

    /// <summary>
    /// Parses typed text into a column value. String columns keep the text as typed (an empty string stays empty);
    /// "&lt;null&gt;" means NULL only when <paramref name="nullMarkerAllowed"/> (grid; the form has a null checkbox).
    /// Non-string columns: blank → NULL. Throws FormatException/OverflowException on invalid input.
    /// </summary>
    public static object Parse(string? text, Type type, CultureInfo culture, bool nullMarkerAllowed = true)
    {
        ArgumentNullException.ThrowIfNull(type);
        text ??= "";
        if (nullMarkerAllowed && text == NullText) return DBNull.Value;
        if (type == typeof(string)) return text;
        if (string.IsNullOrWhiteSpace(text)) return DBNull.Value;
        text = text.Trim();

        if (type == typeof(Guid)) return Guid.Parse(text);
        if (type == typeof(DateTime)) return DateTime.Parse(text, culture);
        if (type == typeof(DateTimeOffset)) return DateTimeOffset.Parse(text, culture);
        if (type == typeof(TimeSpan)) return TimeSpan.Parse(text, culture);
        if (type == typeof(bool))
        {
            if (text == "1") return true;
            if (text == "0") return false;
            return bool.Parse(text);
        }

        return Convert.ChangeType(text, type, culture);
    }

    /// <summary>
    /// Text of one printed cell: NULL → empty; binary → "(BLOB)" or hex; memo columns / long strings → "(MEMO)" or the
    /// text on one line. Never longer than <see cref="MaxPrintChars"/>.
    /// </summary>
    public static string FormatForPrint(object? value, bool isMemoColumn, bool printBlobAndMemo, CultureInfo culture)
    {
        if (IsNull(value)) return "";
        if (value is byte[] b) return printBlobAndMemo ? ToHex(b, MaxPrintChars / 2 - 2) : BlobPlaceholder;
        if (value is string s)
        {
            if (!printBlobAndMemo && (isMemoColumn || s.Length > MemoThreshold)) return MemoPlaceholder;
            return OneLine(s);
        }

        return OneLine(Format(value, culture));
    }

    /// <summary>CR/LF/TAB → single spaces, cut to <see cref="MaxPrintChars"/>.</summary>
    public static string OneLine(string s)
    {
        var limit = Math.Min(s.Length, MaxPrintChars * 2);
        var sb = new StringBuilder(Math.Min(limit, MaxPrintChars));
        var lastSpace = false;
        for (var i = 0; i < limit && sb.Length < MaxPrintChars; i++)
        {
            var ch = s[i];
            if (ch is '\r' or '\n' or '\t')
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
                continue;
            }
            if (char.IsControl(ch)) continue;
            sb.Append(ch);
            lastSpace = false;
        }
        return sb.ToString();
    }
}
