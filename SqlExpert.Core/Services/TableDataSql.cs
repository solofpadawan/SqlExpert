using System.Data;
using System.Text;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>
/// Pure SQL generation for the table Data tab. Every value goes through parameters;
/// identifiers are always bracket-quoted (']' escaped as ']]').
/// </summary>
public static class TableDataSql
{
    /// <summary>Columns of a table for data editing (@schema, @table).</summary>
    public const string TableDataColumns = """
        SELECT c.column_id,
               c.name,
               TYPE_NAME(c.user_type_id) AS type_name,
               ISNULL(TYPE_NAME(c.system_type_id), TYPE_NAME(c.user_type_id)) AS base_type_name,
               c.is_nullable,
               c.is_identity,
               c.is_computed,
               CAST(CASE WHEN c.system_type_id = 189 THEN 1 ELSE 0 END AS bit) AS is_rowversion,
               CAST(CASE WHEN c.system_type_id = 240 THEN 1 ELSE 0 END AS bit) AS is_clr,
               ISNULL(pk.key_ordinal, 0) AS key_ordinal,
               CAST(c.max_length AS int) AS max_length,
               CAST(c.precision AS int) AS precision,
               CAST(c.scale AS int) AS scale
        FROM sys.columns c
        JOIN sys.tables t ON t.object_id = c.object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        LEFT JOIN (
            SELECT ic.object_id, ic.column_id, CAST(ic.key_ordinal AS int) AS key_ordinal
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            WHERE i.is_primary_key = 1
        ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
        WHERE s.name = @schema AND t.name = @table
        ORDER BY c.column_id;
        """;

    public static string Quote(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return "[" + identifier.Replace("]", "]]") + "]";
    }

    public static string QualifiedName(string schema, string table) => Quote(schema) + "." + Quote(table);

    public static string QualifiedName(TableDataSchema s) => QualifiedName(s.Schema, s.Table);

    /// <summary>Select-list expression: CLR UDTs are read as text (ToString()).</summary>
    public static string SelectExpression(DataColumnInfo c) =>
        c.IsClrType ? $"{Quote(c.Name)}.ToString() AS {Quote(c.Name)}" : Quote(c.Name);

    public static string SelectList(TableDataSchema s) =>
        string.Join(", ", s.Columns.OrderBy(c => c.Ordinal).Select(SelectExpression));

    /// <summary>SELECT TOP (@top) … ORDER BY primary key (when present).</summary>
    public static string BuildSelectTop(TableDataSchema s)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT TOP (@top) ").Append(SelectList(s)).Append(" FROM ").Append(QualifiedName(s));
        var keys = s.KeyColumns;
        if (keys.Count > 0)
            sb.Append(" ORDER BY ").Append(string.Join(", ", keys.Select(k => Quote(k.Name))));
        sb.Append(';');
        return sb.ToString();
    }

    private static bool IsNull(object? v) => v is null || v is DBNull;

    private static DataColumnInfo Column(TableDataSchema s, string name) =>
        s.Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Coluna desconhecida: {name}", nameof(name));

    private static string KeyWhere(TableDataSchema s, IReadOnlyDictionary<string, object?> key, List<SqlParam> ps, string prefix)
    {
        var keys = s.KeyColumns;
        if (keys.Count == 0)
            throw new InvalidOperationException($"A tabela {QualifiedName(s)} não tem chave primária.");
        var parts = new List<string>();
        var i = 0;
        foreach (var k in keys)
        {
            if (!key.TryGetValue(k.Name, out var v) || IsNull(v))
                throw new ArgumentException($"Valor da chave primária [{k.Name}] ausente.", nameof(key));
            var p = "@" + prefix + i++;
            ps.Add(new SqlParam(p, v, k));
            parts.Add($"{Quote(k.Name)} = {p}");
        }
        return string.Join(" AND ", parts);
    }

    /// <summary>
    /// INSERT of the writable, non-NULL values (NULL/omitted columns get the server default),
    /// followed by a SELECT of the new row (by SCOPE_IDENTITY() or by the supplied PK) when possible.
    /// </summary>
    public static SqlStatement BuildInsert(TableDataSchema s, IReadOnlyDictionary<string, object?> values)
    {
        var ps = new List<SqlParam>();
        var cols = new List<string>();
        var vals = new List<string>();
        var i = 0;
        foreach (var c in s.Columns.OrderBy(c => c.Ordinal))
        {
            if (!c.IsWritable) continue;
            if (!values.TryGetValue(c.Name, out var v) || IsNull(v)) continue;
            var p = "@p" + i++;
            cols.Add(Quote(c.Name));
            vals.Add(p);
            ps.Add(new SqlParam(p, v, c));
        }

        var sb = new StringBuilder();
        sb.Append("INSERT INTO ").Append(QualifiedName(s));
        if (cols.Count == 0)
            sb.Append(" DEFAULT VALUES;");
        else
            sb.Append(" (").Append(string.Join(", ", cols)).Append(") VALUES (").Append(string.Join(", ", vals)).Append(");");

        var returnsRow = false;
        var identity = s.IdentityColumn;
        if (identity is not null)
        {
            sb.Append(" SELECT ").Append(SelectList(s)).Append(" FROM ").Append(QualifiedName(s))
              .Append(" WHERE ").Append(Quote(identity.Name)).Append(" = SCOPE_IDENTITY();");
            returnsRow = true;
        }
        else if (s.HasPrimaryKey && s.KeyColumns.All(k => values.TryGetValue(k.Name, out var kv) && !IsNull(kv)))
        {
            var where = KeyWhere(s, values, ps, "k");
            sb.Append(" SELECT ").Append(SelectList(s)).Append(" FROM ").Append(QualifiedName(s))
              .Append(" WHERE ").Append(where).Append(';');
            returnsRow = true;
        }

        return new SqlStatement(sb.ToString(), ps, returnsRow);
    }

    /// <summary>UPDATE of the changed writable columns keyed by the original PK; then re-selects the row.</summary>
    public static SqlStatement? BuildUpdate(TableDataSchema s, IReadOnlyDictionary<string, object?> changed, IReadOnlyDictionary<string, object?> originalKey)
    {
        var ps = new List<SqlParam>();
        var sets = new List<string>();
        var i = 0;
        foreach (var (name, v) in changed)
        {
            var c = Column(s, name);
            if (!c.IsWritable) continue;
            var p = "@p" + i++;
            sets.Add($"{Quote(c.Name)} = {p}");
            ps.Add(new SqlParam(p, IsNull(v) ? null : v, c));
        }

        if (sets.Count == 0)
            return null;

        var where = KeyWhere(s, originalKey, ps, "k");
        var sb = new StringBuilder();
        sb.Append("UPDATE ").Append(QualifiedName(s)).Append(" SET ").Append(string.Join(", ", sets))
          .Append(" WHERE ").Append(where).Append(';');

        // New key values (PK may have been edited) for the re-select.
        var newKey = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in s.KeyColumns)
            newKey[k.Name] = changed.TryGetValue(k.Name, out var nv) ? nv : originalKey.GetValueOrDefault(k.Name);
        var where2 = KeyWhere(s, newKey, ps, "n");
        sb.Append(" IF @@ROWCOUNT > 0 SELECT ").Append(SelectList(s)).Append(" FROM ").Append(QualifiedName(s))
          .Append(" WHERE ").Append(where2).Append(';');

        return new SqlStatement(sb.ToString(), ps, true);
    }

    public static SqlStatement BuildDelete(TableDataSchema s, IReadOnlyDictionary<string, object?> originalKey)
    {
        var ps = new List<SqlParam>();
        var where = KeyWhere(s, originalKey, ps, "k");
        return new SqlStatement($"DELETE FROM {QualifiedName(s)} WHERE {where};", ps);
    }

    /// <summary>SqlDbType for a SQL Server base type name; null lets SqlClient infer it.</summary>
    public static SqlDbType? MapSqlDbType(string? baseTypeName) => (baseTypeName ?? "").ToLowerInvariant() switch
    {
        "int" => SqlDbType.Int,
        "bigint" => SqlDbType.BigInt,
        "smallint" => SqlDbType.SmallInt,
        "tinyint" => SqlDbType.TinyInt,
        "bit" => SqlDbType.Bit,
        "decimal" or "numeric" => SqlDbType.Decimal,
        "money" => SqlDbType.Money,
        "smallmoney" => SqlDbType.SmallMoney,
        "float" => SqlDbType.Float,
        "real" => SqlDbType.Real,
        "date" => SqlDbType.Date,
        "datetime" => SqlDbType.DateTime,
        "smalldatetime" => SqlDbType.SmallDateTime,
        "datetime2" => SqlDbType.DateTime2,
        "datetimeoffset" => SqlDbType.DateTimeOffset,
        "time" => SqlDbType.Time,
        "char" => SqlDbType.Char,
        "varchar" => SqlDbType.VarChar,
        "text" => SqlDbType.Text,
        "nchar" => SqlDbType.NChar,
        "nvarchar" => SqlDbType.NVarChar,
        "ntext" => SqlDbType.NText,
        "uniqueidentifier" => SqlDbType.UniqueIdentifier,
        "binary" => SqlDbType.Binary,
        "varbinary" => SqlDbType.VarBinary,
        "image" => SqlDbType.Image,
        "xml" => SqlDbType.Xml,
        _ => null
    };
}
