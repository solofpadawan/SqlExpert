using Microsoft.Data.SqlClient;
using SqlExpert.Core.SqlEditor;

namespace SqlExpert.Core.Services;

/// <summary>Reads the <see cref="CompletionCatalog"/> of the connection's current database (catalog views only).</summary>
public static class CompletionCatalogLoader
{
    /// <summary>User objects of the kinds offered by code completion / object links.</summary>
    public const string ObjectsSql = """
        SELECT s.name, o.name, RTRIM(o.type)
        FROM sys.objects AS o
        JOIN sys.schemas AS s ON s.schema_id = o.schema_id
        WHERE o.is_ms_shipped = 0
          AND o.type IN ('U','V','P','PC','X','FN','IF','TF','FS','FT','AF','TR','TA','SO','SN')
        UNION ALL
        SELECT s.name, t.name, 'TY'
        FROM sys.types AS t
        JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        WHERE t.is_user_defined = 1;
        """;

    /// <summary>Columns of user tables, views and table-valued functions.</summary>
    public const string ColumnsSql = """
        SELECT s.name, o.name, c.name, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale, c.is_nullable, c.column_id
        FROM sys.columns AS c
        JOIN sys.objects AS o ON o.object_id = c.object_id
        JOIN sys.schemas AS s ON s.schema_id = o.schema_id
        WHERE o.is_ms_shipped = 0 AND o.type IN ('U','V','IF','TF','FT');
        """;

    public const string SchemasSql = "SELECT name FROM sys.schemas WHERE schema_id < 16384 AND name NOT IN ('sys','INFORMATION_SCHEMA','guest');";

    public static CompletionKind? KindOf(string type) => type.Trim().ToUpperInvariant() switch
    {
        "U" => CompletionKind.Table,
        "V" => CompletionKind.View,
        "P" or "PC" or "X" => CompletionKind.Procedure,
        "FN" or "IF" or "TF" or "FS" or "FT" or "AF" => CompletionKind.Function,
        "TR" or "TA" => CompletionKind.Trigger,
        "SO" => CompletionKind.Sequence,
        "SN" => CompletionKind.Synonym,
        "TY" => CompletionKind.Type,
        _ => null
    };

    /// <summary>"varchar(50)", "nvarchar(max)", "decimal(15,2)", "int".</summary>
    public static string FormatType(string? typeName, int maxLength, int precision, int scale)
    {
        var t = (typeName ?? "?").ToLowerInvariant();
        return t switch
        {
            "varchar" or "char" or "varbinary" or "binary" => $"{t}({(maxLength == -1 ? "max" : maxLength.ToString())})",
            "nvarchar" or "nchar" => $"{t}({(maxLength == -1 ? "max" : (maxLength / 2).ToString())})",
            "decimal" or "numeric" => $"{t}({precision},{scale})",
            "datetime2" or "time" or "datetimeoffset" when scale != 7 => $"{t}({scale})",
            _ => t
        };
    }

    /// <summary>
    /// Loads the catalog of <paramref name="conn"/>'s current database. A short LOCK_TIMEOUT keeps a session that
    /// holds uncommitted DDL from blocking the editor; on timeout what was read so far is kept (columns may be missing).
    /// </summary>
    public static async Task<CompletionCatalog> LoadAsync(SqlConnection conn, CancellationToken ct = default)
    {
        var database = conn.Database;
        await using (var set = new SqlCommand("SET LOCK_TIMEOUT 3000;", conn))
            await set.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var objects = new List<CatalogObject>();
        await using (var cmd = new SqlCommand(ObjectsSql, conn) { CommandTimeout = 30 })
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var kind = KindOf(r.GetString(2));
                if (kind is null) continue;
                objects.Add(new CatalogObject(r.GetString(0), r.GetString(1), kind.Value));
            }
        }

        var schemas = new List<string>();
        try
        {
            await using var cmd = new SqlCommand(SchemasSql, conn) { CommandTimeout = 15 };
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false)) schemas.Add(r.GetString(0));
        }
        catch (SqlException) { /* optional */ }

        var columns = new List<CatalogColumn>();
        try
        {
            await using var cmd = new SqlCommand(ColumnsSql, conn) { CommandTimeout = 60 };
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var type = FormatType(r.IsDBNull(3) ? null : r.GetString(3), r.GetInt16(4), r.GetByte(5), r.GetByte(6));
                columns.Add(new CatalogColumn(r.GetString(0), r.GetString(1), r.GetString(2), type, r.GetInt32(8), r.GetBoolean(7)));
            }
        }
        catch (SqlException) { /* lock timeout: objects only */ }

        try
        {
            await using var reset = new SqlCommand("SET LOCK_TIMEOUT -1;", conn);
            await reset.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException) { /* the pool resets it anyway */ }

        return new CompletionCatalog(database, objects, columns, schemas);
    }
}
