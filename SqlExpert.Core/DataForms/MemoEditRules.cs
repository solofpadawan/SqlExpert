using System.Globalization;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.DataForms;

/// <summary>
/// Rules of the "..." memo window of text columns (Data grid, Form View, SQL Editor results). Pure logic, no WPF.
/// </summary>
public static class MemoEditRules
{
    private static readonly HashSet<string> TextTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "char", "varchar", "nchar", "nvarchar", "text", "ntext"
    };

    /// <summary>char, varchar, nchar, nvarchar (sized or MAX), text and ntext. "nvarchar(max)" and alias base types work too.</summary>
    public static bool IsTextTypeName(string? sqlTypeName)
    {
        var t = (sqlTypeName ?? "").Trim();
        var p = t.IndexOf('(');
        if (p >= 0) t = t[..p].Trim();
        return TextTypes.Contains(t);
    }

    public static bool IsTextColumn(DataColumnInfo c) =>
        !c.IsClrType && IsTextTypeName(string.IsNullOrEmpty(c.BaseTypeName) ? c.TypeName : c.BaseTypeName);

    /// <summary>Character limit of the editor: the declared length of sized columns; 0 (no limit) for MAX, text, ntext.</summary>
    public static int MaxChars(DataColumnInfo c)
    {
        if (!IsTextColumn(c)) return 0;
        var b = (string.IsNullOrEmpty(c.BaseTypeName) ? c.TypeName : c.BaseTypeName).Trim().ToLowerInvariant();
        if (b is "text" or "ntext") return 0;
        var len = FormFieldLayout.CharLength(c);
        return len > 0 ? len : 0;
    }

    /// <summary>Text shown when the window loads a value: NULL opens EMPTY.</summary>
    public static string EditorText(object? value) => value switch
    {
        null or DBNull => "",
        string s => s,
        _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? ""
    };

    /// <summary>
    /// Value to write back for the edited text, or false when the cell must stay as it is: not edited, NULL left blank
    /// (stays NULL, consistent with the grid), or the same text as before.
    /// </summary>
    public static bool TryResolve(object? original, string? text, bool edited, out object value)
    {
        value = DBNull.Value;
        text ??= "";
        if (!edited) return false;
        if (DataValueText.KeepsNullWhenBlank(original, text)) return false;
        if (original is string s && string.Equals(s, text, StringComparison.Ordinal)) return false;
        value = text;
        return true;
    }

    /// <summary>The live counter of the "Plain text editor" header: "0 chars", "1 char", "1.234 chars".</summary>
    public static string CharCounter(string? text, CultureInfo? culture = null)
    {
        var n = text?.Length ?? 0;
        return n == 1 ? "1 char" : n.ToString("N0", culture ?? CultureInfo.CurrentCulture) + " chars";
    }
}
