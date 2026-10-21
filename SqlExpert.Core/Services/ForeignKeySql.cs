using System.Text;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>Pure SQL for the foreign-key lookup of the Data tab (identifiers bracket-quoted, values parameterized).</summary>
public static class ForeignKeySql
{
    /// <summary>
    /// Foreign keys of a table (@schema, @table), one row per FK column:
    /// name, referenced schema, referenced table, constraint_column_id, local column, referenced column, is_disabled.
    /// </summary>
    public const string TableForeignKeys = """
        SELECT fk.name,
               rs.name AS referenced_schema,
               ro.name AS referenced_table,
               CAST(fkc.constraint_column_id AS int) AS column_id,
               pc.name AS column_name,
               rc.name AS referenced_column,
               fk.is_disabled
        FROM sys.foreign_keys fk
        JOIN sys.tables t ON t.object_id = fk.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.objects ro ON ro.object_id = fk.referenced_object_id
        JOIN sys.schemas rs ON rs.schema_id = ro.schema_id
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE s.name = @schema AND t.name = @table
        ORDER BY fk.name, fkc.constraint_column_id;
        """;

    /// <summary>
    /// SELECT [TOP (@top)] keys + display columns FROM referenced table ORDER BY keys.
    /// <paramref name="top"/> &lt;= 0 reads every row ("Fetch all").
    /// </summary>
    public static string BuildLookupSelect(ForeignKeyInfo fk, IReadOnlyList<DataColumnInfo> columns, int keyCount, int top)
    {
        if (columns.Count == 0 || keyCount <= 0 || keyCount > columns.Count)
            throw new ArgumentException("Lookup sem colunas de chave.", nameof(columns));
        var sb = new StringBuilder("SELECT ");
        if (top > 0) sb.Append("TOP (@top) ");
        sb.Append(string.Join(", ", columns.Select(TableDataSql.SelectExpression)));
        sb.Append(" FROM ").Append(TableDataSql.QualifiedName(fk.ReferencedSchema, fk.ReferencedTable));
        sb.Append(" ORDER BY ").Append(string.Join(", ", columns.Take(keyCount).Select(c => TableDataSql.Quote(c.Name))));
        sb.Append(';');
        return sb.ToString();
    }

    /// <summary>SELECT of the single referenced row whose key equals <paramref name="key"/> (@k0, @k1, …).</summary>
    public static SqlStatement BuildLookupRowByKey(ForeignKeyInfo fk, IReadOnlyList<DataColumnInfo> columns, int keyCount, IReadOnlyList<object?> key)
    {
        if (key.Count != keyCount)
            throw new ArgumentException("Quantidade de valores da chave não confere.", nameof(key));
        var ps = new List<SqlParam>();
        var where = new List<string>();
        for (var i = 0; i < keyCount; i++)
        {
            var p = "@k" + i;
            ps.Add(new SqlParam(p, key[i], columns[i]));
            where.Add($"{TableDataSql.Quote(columns[i].Name)} = {p}");
        }
        var sql = "SELECT " + string.Join(", ", columns.Select(TableDataSql.SelectExpression))
                  + " FROM " + TableDataSql.QualifiedName(fk.ReferencedSchema, fk.ReferencedTable)
                  + " WHERE " + string.Join(" AND ", where) + ";";
        return new SqlStatement(sql, ps);
    }
}
