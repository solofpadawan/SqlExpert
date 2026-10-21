using System.Data;
using Microsoft.Data.SqlClient;
using SqlExpert.Core.Abstractions;
using SqlExpert.Core.DataForms;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

public sealed class TableDataService : ITableDataService
{
    private readonly ISqlConnectionFactory _connections;

    public TableDataService(ISqlConnectionFactory connections) => _connections = connections;

    public async Task<TableDataSchema> GetSchemaAsync(
        string connectionProfileId, string database, string schema, string table, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(connectionProfileId, database, ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(TableDataSql.TableDataColumns, conn);
        cmd.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = schema });
        cmd.Parameters.Add(new SqlParameter("@table", SqlDbType.NVarChar, 128) { Value = table });
        var cols = new List<DataColumnInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            cols.Add(new DataColumnInfo
            {
                Ordinal = r.GetInt32(0),
                Name = r.GetString(1),
                TypeName = r.IsDBNull(2) ? "" : r.GetString(2),
                BaseTypeName = r.IsDBNull(3) ? "" : r.GetString(3),
                IsNullable = r.GetBoolean(4),
                IsIdentity = r.GetBoolean(5),
                IsComputed = r.GetBoolean(6),
                IsRowVersion = r.GetBoolean(7),
                IsClrType = r.GetBoolean(8),
                KeyOrdinal = r.GetInt32(9),
                IsPrimaryKey = r.GetInt32(9) > 0,
                MaxLength = r.FieldCount > 10 && !r.IsDBNull(10) ? r.GetInt32(10) : 0,
                Precision = r.FieldCount > 11 && !r.IsDBNull(11) ? r.GetInt32(11) : 0,
                Scale = r.FieldCount > 12 && !r.IsDBNull(12) ? r.GetInt32(12) : 0
            });
        }

        if (cols.Count == 0)
            throw new InvalidOperationException($"Tabela não encontrada: {TableDataSql.QualifiedName(schema, table)}");

        await r.DisposeAsync().ConfigureAwait(false);

        // Foreign keys drive the Data tab lookups; without permission (or on error) the grid simply has none.
        IReadOnlyList<ForeignKeyInfo> fks;
        try { fks = await ReadForeignKeysAsync(conn, schema, table, ct).ConfigureAwait(false); }
        catch (SqlException) { fks = Array.Empty<ForeignKeyInfo>(); }

        return new TableDataSchema { Schema = schema, Table = table, Columns = cols, ForeignKeys = fks };
    }

    public async Task<TableDataPage> LoadAsync(
        string connectionProfileId, string database, TableDataSchema schema, int top, CancellationToken ct = default)
    {
        if (top <= 0) top = 1000;
        await using var conn = await OpenAsync(connectionProfileId, database, ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(TableDataSql.BuildSelectTop(schema), conn);
        cmd.Parameters.Add(new SqlParameter("@top", SqlDbType.Int) { Value = top });
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var types = new Type[r.FieldCount];
        for (var i = 0; i < r.FieldCount; i++)
            types[i] = r.GetFieldType(i);

        var rows = new List<object[]>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var values = new object[r.FieldCount];
            r.GetValues(values);
            rows.Add(values);
        }

        return new TableDataPage { Schema = schema, FieldTypes = types, Rows = rows, Top = top };
    }

    public async Task<IReadOnlyList<object[]?>> ApplyChangesAsync(
        string connectionProfileId, string database, TableDataSchema schema, IReadOnlyList<RowChange> changes, CancellationToken ct = default)
    {
        var results = new List<object[]?>();
        if (changes.Count == 0) return results;

        await using var conn = await OpenAsync(connectionProfileId, database, ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var change in changes)
            {
                switch (change.Kind)
                {
                    case RowChangeKind.Insert:
                        results.Add(await ExecuteReturningRowAsync(conn, tx, TableDataSql.BuildInsert(schema, change.Values), ct).ConfigureAwait(false));
                        break;
                    case RowChangeKind.Update:
                    {
                        var stmt = TableDataSql.BuildUpdate(schema, change.Values, change.Key);
                        if (stmt is null) { results.Add(null); break; }
                        var row = await ExecuteReturningRowAsync(conn, tx, stmt, ct, requireAffected: true).ConfigureAwait(false);
                        results.Add(row);
                        break;
                    }
                    case RowChangeKind.Delete:
                    {
                        var stmt = TableDataSql.BuildDelete(schema, change.Key);
                        await using var cmd = CreateCommand(conn, tx, stmt);
                        var n = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        if (n == 0)
                            throw new InvalidOperationException("O registro não foi encontrado no servidor (foi alterado ou removido por outro usuário?).");
                        results.Add(null);
                        break;
                    }
                }
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return results;
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { /* connection may be broken */ }
            throw;
        }
    }

    public async Task<IReadOnlyList<ForeignKeyInfo>> GetForeignKeysAsync(
        string connectionProfileId, string database, string schema, string table, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(connectionProfileId, database, ct).ConfigureAwait(false);
        return await ReadForeignKeysAsync(conn, schema, table, ct).ConfigureAwait(false);
    }

    /// <summary>FK metadata of a table on an open connection (shared with the integration tests).</summary>
    public static async Task<IReadOnlyList<ForeignKeyInfo>> ReadForeignKeysAsync(SqlConnection conn, string schema, string table, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand(ForeignKeySql.TableForeignKeys, conn);
        cmd.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = schema });
        cmd.Parameters.Add(new SqlParameter("@table", SqlDbType.NVarChar, 128) { Value = table });
        var rows = new List<ForeignKeyColumnRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new ForeignKeyColumnRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetString(4), r.GetString(5), r.GetBoolean(6)));
        return ForeignKeyLookup.Group(rows);
    }

    public async Task<LookupData> GetLookupDataAsync(
        string connectionProfileId, string database, ForeignKeyInfo fk, int top, IReadOnlyList<object?>? currentKey, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(connectionProfileId, database, ct).ConfigureAwait(false);
        return await ReadLookupDataAsync(conn, fk, top, currentKey, ct).ConfigureAwait(false);
    }

    /// <summary>Lookup rows on an open connection (read-only SELECTs only).</summary>
    public static async Task<LookupData> ReadLookupDataAsync(
        SqlConnection conn, ForeignKeyInfo fk, int top, IReadOnlyList<object?>? currentKey, CancellationToken ct = default)
    {
        var refColumns = await ReadColumnsAsync(conn, fk.ReferencedSchema, fk.ReferencedTable, ct).ConfigureAwait(false);
        if (refColumns.Count == 0)
            throw new InvalidOperationException($"Tabela referenciada não encontrada: {fk.ReferencedTitle}");
        var columns = ForeignKeyLookup.SelectDisplayColumns(refColumns, fk.ReferencedColumns);
        var keyCount = fk.Columns.Count;

        var rows = new List<object[]>();
        await using (var cmd = new SqlCommand(ForeignKeySql.BuildLookupSelect(fk, columns, keyCount, top), conn))
        {
            if (top > 0) cmd.Parameters.Add(new SqlParameter("@top", SqlDbType.Int) { Value = top });
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var values = new object[r.FieldCount];
                r.GetValues(values);
                rows.Add(values);
            }
        }

        var complete = top <= 0 || rows.Count < top;
        var appended = false;
        if (!complete && currentKey is { Count: > 0 } && currentKey.Count == keyCount
            && !currentKey.Any(DataValueText.IsNull) && ForeignKeyLookup.FindRowIndex(rows, currentKey) < 0)
        {
            var stmt = ForeignKeySql.BuildLookupRowByKey(fk, columns, keyCount, currentKey);
            await using var cmd = CreateCommand(conn, null, stmt);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var values = new object[r.FieldCount];
                r.GetValues(values);
                rows.Add(values);
                appended = true;
            }
        }

        return new LookupData
        {
            ForeignKey = fk,
            Columns = columns,
            KeyCount = keyCount,
            Rows = rows,
            Top = top > 0 ? top : 0,
            IsComplete = complete,
            CurrentRowAppended = appended
        };
    }

    private static async Task<List<DataColumnInfo>> ReadColumnsAsync(SqlConnection conn, string schema, string table, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(TableDataSql.TableDataColumns, conn);
        cmd.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = schema });
        cmd.Parameters.Add(new SqlParameter("@table", SqlDbType.NVarChar, 128) { Value = table });
        var cols = new List<DataColumnInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            cols.Add(MapColumn(r));
        return cols;
    }

    private static DataColumnInfo MapColumn(SqlDataReader r) => new()
    {
        Ordinal = r.GetInt32(0),
        Name = r.GetString(1),
        TypeName = r.IsDBNull(2) ? "" : r.GetString(2),
        BaseTypeName = r.IsDBNull(3) ? "" : r.GetString(3),
        IsNullable = r.GetBoolean(4),
        IsIdentity = r.GetBoolean(5),
        IsComputed = r.GetBoolean(6),
        IsRowVersion = r.GetBoolean(7),
        IsClrType = r.GetBoolean(8),
        KeyOrdinal = r.GetInt32(9),
        IsPrimaryKey = r.GetInt32(9) > 0,
        MaxLength = r.FieldCount > 10 && !r.IsDBNull(10) ? r.GetInt32(10) : 0,
        Precision = r.FieldCount > 11 && !r.IsDBNull(11) ? r.GetInt32(11) : 0,
        Scale = r.FieldCount > 12 && !r.IsDBNull(12) ? r.GetInt32(12) : 0
    };

    private static async Task<object[]?> ExecuteReturningRowAsync(
        SqlConnection conn, SqlTransaction tx, SqlStatement stmt, CancellationToken ct, bool requireAffected = false)
    {
        await using var cmd = CreateCommand(conn, tx, stmt);
        if (!stmt.ReturnsRow)
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return null;
        }

        object[]? row = null;
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            // Skip result sets produced before our SELECT (e.g. by triggers) – take the last one with our columns.
            do
            {
                while (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    var values = new object[r.FieldCount];
                    r.GetValues(values);
                    row = values;
                }
            } while (await r.NextResultAsync(ct).ConfigureAwait(false));
        }

        if (requireAffected && row is null)
            throw new InvalidOperationException("O registro não foi encontrado no servidor (foi alterado ou removido por outro usuário?).");
        return row;
    }

    private static SqlCommand CreateCommand(SqlConnection conn, SqlTransaction? tx, SqlStatement stmt)
    {
        var cmd = new SqlCommand(stmt.Sql, conn, tx);
        foreach (var p in stmt.Parameters)
        {
            var param = new SqlParameter { ParameterName = p.Name, Value = p.Value ?? DBNull.Value };
            var type = TableDataSql.MapSqlDbType(p.Column.BaseTypeName);
            if (type is not null)
                param.SqlDbType = type.Value;
            cmd.Parameters.Add(param);
        }
        return cmd;
    }

    private async Task<SqlConnection> OpenAsync(string profileId, string database, CancellationToken ct)
    {
        var conn = await _connections.CreateOpenConnectionAsync(profileId, ct).ConfigureAwait(false);
        try
        {
            await conn.ChangeDatabaseAsync(database, ct).ConfigureAwait(false);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
