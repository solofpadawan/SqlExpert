using Microsoft.Data.SqlClient;
using SqlExpert.Core.Abstractions;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

public sealed class MetadataService : IMetadataService
{
    private readonly ISqlConnectionFactory _connections;

    public MetadataService(ISqlConnectionFactory connections) => _connections = connections;

    public async Task<IReadOnlyList<string>> ListDatabasesAsync(string connectionProfileId, CancellationToken ct = default)
    {
        await using var conn = await _connections.CreateOpenConnectionAsync(connectionProfileId, ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(MetadataSql.ListDatabases, conn);
        var list = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            list.Add(reader.GetString(0));
        return list;
    }

    public async Task<IReadOnlyList<SchemaNode>> GetObjectTreeAsync(
        string connectionProfileId,
        string database,
        CancellationToken ct = default)
    {
        await using var conn = await _connections.CreateOpenConnectionAsync(connectionProfileId, ct).ConfigureAwait(false);
        await conn.ChangeDatabaseAsync(database, ct).ConfigureAwait(false);

        var nodes = new List<SchemaNode>();
        foreach (var kind in MetadataSql.ExplorerKinds)
        {
            var count = await ExecuteScalarIntAsync(conn, MetadataSql.CountByKind(kind), ct).ConfigureAwait(false);
            var label = MetadataSql.CategoryDisplayName(kind);
            nodes.Add(new SchemaNode
            {
                Name = label, // count is drawn separately (ExplorerNodeText.CountText)
                Kind = kind,
                Count = count
            });
        }

        return nodes;
    }

    public async Task<IReadOnlyList<ObjectRef>> ListObjectsAsync(
        string connectionProfileId,
        string database,
        ObjectKind kind,
        CancellationToken ct = default)
    {
        await using var conn = await _connections.CreateOpenConnectionAsync(connectionProfileId, ct).ConfigureAwait(false);
        await conn.ChangeDatabaseAsync(database, ct).ConfigureAwait(false);

        await using var cmd = new SqlCommand(MetadataSql.ListByKind(kind), conn);
        var list = new List<ObjectRef>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var schema = reader.IsDBNull(0) ? "dbo" : reader.GetString(0);
            var name = reader.GetString(1);
            list.Add(new ObjectRef(database, schema, name, kind));
        }
        await reader.CloseAsync().ConfigureAwait(false);

        if (list.Count == 0 || !MetadataSql.HasObjectDescriptions(kind))
            return list;

        // MS_Description for "Show objects description" (optional: errors leave the names alone).
        try
        {
            var descriptions = new Dictionary<(string, string), string>();
            await using var dcmd = new SqlCommand(MetadataSql.ObjectDescriptions, conn);
            await using var dr = await dcmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await dr.ReadAsync(ct).ConfigureAwait(false))
            {
                if (dr.IsDBNull(0) || dr.IsDBNull(1) || dr.IsDBNull(2)) continue;
                descriptions[(dr.GetString(0).ToUpperInvariant(), dr.GetString(1).ToUpperInvariant())] = dr.GetString(2);
            }
            if (descriptions.Count == 0) return list;
            return list.Select(o => descriptions.TryGetValue((o.Schema.ToUpperInvariant(), o.Name.ToUpperInvariant()), out var d)
                ? o with { Description = d }
                : o).ToList();
        }
        catch (SqlException)
        {
            return list;
        }
    }

    public async Task<TableMetadata?> GetTableAsync(
        string connectionProfileId,
        string database,
        string schema,
        string table,
        CancellationToken ct = default)
    {
        await using var conn = await _connections.CreateOpenConnectionAsync(connectionProfileId, ct).ConfigureAwait(false);
        await conn.ChangeDatabaseAsync(database, ct).ConfigureAwait(false);

        var columns = await ReadColumnsAsync(conn, schema, table, ct).ConfigureAwait(false);
        if (columns.Count == 0)
            return null;

        var constraints = await ReadConstraintsAsync(conn, schema, table, ct).ConfigureAwait(false);
        var indices = await ReadIndexesAsync(conn, schema, table, ct).ConfigureAwait(false);
        var description = await ReadTableDescriptionAsync(conn, schema, table, ct).ConfigureAwait(false);

        return new TableMetadata
        {
            Ref = new ObjectRef(database, schema, table, ObjectKind.Table),
            Columns = columns,
            Constraints = constraints,
            Indices = indices,
            Description = description
        };
    }

    public async Task<string> ScriptObjectDdlAsync(
        string connectionProfileId,
        ObjectRef objectRef,
        CancellationToken ct = default)
    {
        if (objectRef.Kind == ObjectKind.Table)
        {
            var meta = await GetTableAsync(connectionProfileId, objectRef.Database, objectRef.Schema, objectRef.Name, ct)
                .ConfigureAwait(false);
            if (meta is null)
                return $"-- Table [{objectRef.Schema}].[{objectRef.Name}] not found\n";
            return DdlScriptBuilder.BuildCreateTable(meta);
        }

        await using var conn = await _connections.CreateOpenConnectionAsync(connectionProfileId, ct).ConfigureAwait(false);
        await conn.ChangeDatabaseAsync(objectRef.Database, ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            "SELECT OBJECT_DEFINITION(OBJECT_ID(@fullName));",
            conn);
        cmd.Parameters.AddWithValue("@fullName", $"{objectRef.Schema}.{objectRef.Name}");
        var def = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return string.IsNullOrWhiteSpace(def)
            ? $"-- No OBJECT_DEFINITION for {objectRef.Kind} [{objectRef.Schema}].[{objectRef.Name}]\n"
            : def + (def.EndsWith('\n') ? "" : "\n");
    }

    private static async Task<int> ExecuteScalarIntAsync(SqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    private static async Task<List<ColumnInfo>> ReadColumnsAsync(
        SqlConnection conn, string schema, string table, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(MetadataSql.TableColumns, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);
        var list = new List<ColumnInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new ColumnInfo
            {
                Ordinal = r.GetInt32(0),
                Name = r.GetString(1),
                IsPrimaryKey = r.GetInt32(2) == 1,
                IsForeignKey = r.GetInt32(3) == 1,
                IsUnique = r.GetInt32(4) == 1,
                DataType = r.GetString(5),
                Size = r.IsDBNull(6) ? null : r.GetInt32(6),
                Scale = r.IsDBNull(7) ? null : Convert.ToInt32(r.GetValue(7)),
                IsNullable = r.GetBoolean(8),
                DefaultValue = r.IsDBNull(9) ? null : r.GetString(9),
                Collation = r.IsDBNull(10) ? null : r.GetString(10),
                Description = r.IsDBNull(11) ? null : r.GetString(11),
                Precision = r.FieldCount > 12 && !r.IsDBNull(12) ? r.GetInt32(12) : null
            });
        }

        return list;
    }

    private static async Task<List<ConstraintInfo>> ReadConstraintsAsync(
        SqlConnection conn, string schema, string table, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(MetadataSql.TableConstraints, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);
        var list = new List<ConstraintInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new ConstraintInfo
            {
                Name = r.GetString(0),
                Type = r.GetString(1),
                Definition = r.IsDBNull(2) ? "" : r.GetString(2)
            });
        }

        return list;
    }

    private static async Task<List<IndexInfo>> ReadIndexesAsync(
        SqlConnection conn, string schema, string table, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(MetadataSql.TableIndexes, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);
        var list = new List<IndexInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new IndexInfo
            {
                Name = r.GetString(0),
                IsUnique = r.GetBoolean(1),
                IsClustered = r.GetInt32(2) == 1,
                Columns = r.IsDBNull(3) ? "" : r.GetString(3)
            });
        }

        return list;
    }

    private static async Task<string?> ReadTableDescriptionAsync(
        SqlConnection conn, string schema, string table, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(MetadataSql.TableDescription, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is null or DBNull ? null : Convert.ToString(result);
    }
}
