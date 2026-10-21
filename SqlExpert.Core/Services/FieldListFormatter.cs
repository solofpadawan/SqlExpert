using System.Text;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>Formats the Fields grid as tab-separated text ("Copy fields list to clipboard").</summary>
public static class FieldListFormatter
{
    public static readonly string[] Header =
        { "#", "PK", "FK", "UNQ", "Field Name", "Field Type", "Size", "Scale", "Null", "Default", "Collation", "Description" };

    /// <summary>Size as shown in the grid: "max" for -1, precision for decimal/numeric.</summary>
    public static string SizeText(ColumnInfo c)
    {
        if (c.Size is -1) return "max";
        if (c.Size is int s) return s.ToString();
        if (SqlTypeCatalog.HasPrecisionAndScale(c.DataType) && c.Precision is int p) return p.ToString();
        return "";
    }

    /// <summary>Scale as shown in the grid (only for types where it means something).</summary>
    public static string ScaleText(ColumnInfo c) =>
        (SqlTypeCatalog.HasPrecisionAndScale(c.DataType) || SqlTypeCatalog.HasFractionalScale(c.DataType)) && c.Scale is int s
            ? s.ToString() : "";

    public static string ToTabSeparated(IEnumerable<ColumnInfo> columns)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join('\t', Header)).Append("\r\n");
        foreach (var c in columns.OrderBy(c => c.Ordinal))
        {
            var cells = new[]
            {
                c.Ordinal.ToString(),
                c.IsPrimaryKey ? "PK" : "",
                c.IsForeignKey ? "FK" : "",
                c.IsUnique ? "UNQ" : "",
                c.Name,
                c.DataType,
                SizeText(c),
                ScaleText(c),
                c.IsNullable ? "NULL" : "NOT NULL",
                c.DefaultValue ?? "",
                c.Collation ?? "",
                c.Description ?? ""
            };
            sb.Append(string.Join('\t', cells.Select(Clean))).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string Clean(string s) => s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
}
