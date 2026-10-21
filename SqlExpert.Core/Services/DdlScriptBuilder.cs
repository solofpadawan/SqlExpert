using System.Text;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>Builds good-enough v1 CREATE TABLE scripts from <see cref="TableMetadata"/>.</summary>
public static class DdlScriptBuilder
{
    public static string BuildCreateTable(TableMetadata table)
    {
        var schema = Bracket(table.Ref.Schema);
        var name = Bracket(table.Ref.Name);
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE {schema}.{name}");
        sb.AppendLine("(");

        var lines = new List<string>();
        foreach (var col in table.Columns.OrderBy(c => c.Ordinal))
        {
            var type = FormatType(col);
            var nullability = col.IsNullable ? "NULL" : "NOT NULL";
            var def = string.IsNullOrWhiteSpace(col.DefaultValue) ? "" : $" DEFAULT {col.DefaultValue}";
            lines.Add($"    {Bracket(col.Name)} {type} {nullability}{def}");
        }

        var pkCols = table.Columns.Where(c => c.IsPrimaryKey).OrderBy(c => c.Ordinal).Select(c => Bracket(c.Name)).ToList();
        if (pkCols.Count > 0)
            lines.Add($"    CONSTRAINT {Bracket("PK_" + table.Ref.Name)} PRIMARY KEY ({string.Join(", ", pkCols)})");

        for (var i = 0; i < lines.Count; i++)
        {
            var comma = i < lines.Count - 1 ? "," : "";
            sb.AppendLine(lines[i] + comma);
        }

        sb.AppendLine(");");

        if (!string.IsNullOrWhiteSpace(table.Description))
        {
            sb.AppendLine();
            sb.AppendLine($"EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N{Quote(table.Description)},");
            sb.AppendLine($"    @level0type=N'SCHEMA', @level0name={QuoteN(table.Ref.Schema)},");
            sb.AppendLine($"    @level1type=N'TABLE',  @level1name={QuoteN(table.Ref.Name)};");
        }

        foreach (var col in table.Columns.Where(c => !string.IsNullOrWhiteSpace(c.Description)))
        {
            sb.AppendLine();
            sb.AppendLine($"EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N{Quote(col.Description!)},");
            sb.AppendLine($"    @level0type=N'SCHEMA', @level0name={QuoteN(table.Ref.Schema)},");
            sb.AppendLine($"    @level1type=N'TABLE',  @level1name={QuoteN(table.Ref.Name)},");
            sb.AppendLine($"    @level2type=N'COLUMN', @level2name={QuoteN(col.Name)};");
        }

        return sb.ToString();
    }

    public static string FormatType(ColumnInfo col)
    {
        var t = col.DataType.ToLowerInvariant();
        return t switch
        {
            "nvarchar" or "nchar" or "varchar" or "char" or "varbinary" or "binary" =>
                col.Size is null ? t
                : col.Size < 0 ? $"{t}(max)"
                : $"{t}({col.Size})",
            "decimal" or "numeric" =>
                (col.Size ?? col.Precision) is int p ? $"{t}({p},{col.Scale ?? 0})" : t,
            "datetime2" or "time" or "datetimeoffset" =>
                col.Scale is null ? t : $"{t}({col.Scale})",
            _ => col.DataType
        };
    }

    private static string Bracket(string id) => "[" + id.Replace("]", "]]") + "]";
    private static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
    private static string QuoteN(string s) => "N'" + s.Replace("'", "''") + "'";
}
