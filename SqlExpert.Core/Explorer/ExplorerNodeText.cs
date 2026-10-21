using System.Globalization;

namespace SqlExpert.Core.Explorer;

/// <summary>
/// Caption pieces of a DB Explorer node. The name and the object count are separate texts so the
/// tree can draw the count (classic-style) in its own color: "Tables" + " (505)".
/// </summary>
public static class ExplorerNodeText
{
    /// <summary>" (505)" with the leading space and parentheses, or null when there is no count.</summary>
    public static string? CountText(int? count) =>
        count is int c ? " (" + c.ToString(CultureInfo.InvariantCulture) + ")" : null;

    /// <summary>
    /// Count while the Explorer filter is active: " (3/505)" (matching / total) when the folder is
    /// filtered, the plain " (505)" when <paramref name="filtered"/> is null.
    /// </summary>
    public static string? CountText(int? count, int? filtered)
    {
        if (filtered is not int f) return CountText(count);
        return count is int c
            ? " (" + f.ToString(CultureInfo.InvariantCulture) + "/" + c.ToString(CultureInfo.InvariantCulture) + ")"
            : " (" + f.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Plain one-line caption ("Tables (505)") for status bar, logs and copy.</summary>
    public static string FullText(string name, int? count, int? filtered = null) => name + (CountText(count, filtered) ?? "");
}
