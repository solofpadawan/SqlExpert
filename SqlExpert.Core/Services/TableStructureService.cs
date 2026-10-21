using Microsoft.Data.SqlClient;
using SqlExpert.Core.Abstractions;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

public sealed class TableStructureService : ITableStructureService
{
    private readonly ISqlConnectionFactory _connections;

    public TableStructureService(ISqlConnectionFactory connections) => _connections = connections;

    public async Task<TableStructure?> LoadAsync(string connectionProfileId, string database, string schema, string table, CancellationToken ct = default)
    {
        await using var conn = await _connections.CreateOpenConnectionAsync(connectionProfileId, ct).ConfigureAwait(false);
        await conn.ChangeDatabaseAsync(database, ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(TableStructureSql.Load, conn);
        cmd.Parameters.Add(new SqlParameter("@schema", System.Data.SqlDbType.NVarChar, 128) { Value = schema });
        cmd.Parameters.Add(new SqlParameter("@table", System.Data.SqlDbType.NVarChar, 128) { Value = table });
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        // 0: header
        if (!await r.ReadAsync(ct).ConfigureAwait(false) || r.IsDBNull(0)) return null;
        var t = new TableStructure
        {
            Schema = schema,
            Name = table,
            Description = Str(r, 1),
            RowCount = r.IsDBNull(2) ? 0 : r.GetInt64(2),
            IdentityCurrent = r.IsDBNull(3) ? null : r.GetDecimal(3)
        };

        // 1: columns
        await r.NextResultAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var typeName = r.GetString(2);
            var userType = r.GetBoolean(4);
            var len = r.GetInt32(5);
            t.Columns.Add(new ColumnStructure
            {
                Ordinal = r.GetInt32(0),
                Name = r.GetString(1),
                TypeName = typeName,
                TypeSchema = r.GetString(3),
                IsUserDefinedType = userType,
                Length = !userType && (SqlTypeCatalog.HasLength(typeName) || len == -1) ? len : null,
                Precision = r.GetInt32(6),
                Scale = r.GetInt32(7),
                IsNullable = r.GetBoolean(8),
                Collation = Str(r, 9),
                IsIdentity = r.GetBoolean(10),
                IdentitySeed = r.IsDBNull(11) ? null : r.GetDecimal(11),
                IdentityIncrement = r.IsDBNull(12) ? null : r.GetDecimal(12),
                IsComputed = r.GetBoolean(13),
                ComputedDefinition = Str(r, 14),
                IsPersisted = r.GetBoolean(15),
                DefaultName = Str(r, 16),
                DefaultDefinition = Str(r, 17),
                DefaultIsSystemNamed = r.GetBoolean(18),
                Description = Str(r, 19)
            });
        }

        // 2: key constraints
        await r.NextResultAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = r.GetString(0);
            var k = t.KeyConstraints.FirstOrDefault(x => x.Name == name);
            if (k is null)
            {
                k = new KeyConstraintStructure { Name = name, IsPrimaryKey = r.GetInt32(1) == 1, IsClustered = r.GetInt32(2) == 1 };
                t.KeyConstraints.Add(k);
            }
            k.Columns.Add(new IndexColumnRef(r.GetString(3), r.GetBoolean(4)));
        }

        // 3: checks
        await r.NextResultAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            t.CheckConstraints.Add(new CheckConstraintStructure
            {
                Name = r.GetString(0),
                Definition = Str(r, 1) ?? "",
                ColumnName = Str(r, 2),
                IsNotTrusted = r.GetBoolean(3),
                IsDisabled = r.GetBoolean(4)
            });
        }

        // 4: foreign keys
        await r.NextResultAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = r.GetString(0);
            var parentSchema = r.GetString(1);
            var fk = t.ForeignKeys.FirstOrDefault(x => x.Name == name && x.ParentSchema == parentSchema);
            if (fk is null)
            {
                fk = new ForeignKeyStructure
                {
                    Name = name,
                    ParentSchema = parentSchema,
                    ParentTable = r.GetString(2),
                    ReferencedSchema = r.GetString(4),
                    ReferencedTable = r.GetString(5),
                    DeleteAction = r.GetString(7),
                    UpdateAction = r.GetString(8),
                    IsNotTrusted = r.GetBoolean(9),
                    IsDisabled = r.GetBoolean(10),
                    IsOutgoing = r.GetInt32(11) == 1
                };
                t.ForeignKeys.Add(fk);
            }
            fk.Columns.Add(r.GetString(3));
            fk.ReferencedColumns.Add(r.GetString(6));
        }

        // 5: indexes
        await r.NextResultAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = r.GetString(0);
            var ix = t.Indexes.FirstOrDefault(x => x.Name == name);
            if (ix is null)
            {
                ix = new IndexStructure
                {
                    Name = name,
                    IsUnique = r.GetBoolean(1),
                    Type = r.GetInt32(2),
                    TypeDesc = r.GetString(3),
                    FilterDefinition = Str(r, 4)
                };
                t.Indexes.Add(ix);
            }
            var col = r.GetString(5);
            if (r.GetBoolean(6)) ix.IncludedColumns.Add(col);
            else ix.KeyColumns.Add(new IndexColumnRef(col, r.GetBoolean(7)));
        }

        // 6: triggers
        await r.NextResultAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            t.Triggers.Add(new TriggerStructure { Name = r.GetString(0), Definition = Str(r, 1), IsDisabled = r.GetBoolean(2) });

        return t;
    }

    public async Task<IReadOnlyList<string>> GetCollationsAsync(string connectionProfileId, string database, CancellationToken ct = default)
    {
        await using var conn = await _connections.CreateOpenConnectionAsync(connectionProfileId, ct).ConfigureAwait(false);
        await conn.ChangeDatabaseAsync(database, ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(TableStructureSql.Collations, conn);
        var list = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            if (!r.IsDBNull(0)) list.Add(r.GetString(0));
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task ExecuteInTransactionAsync(string connectionProfileId, string database, IReadOnlyList<string> statements, CancellationToken ct = default)
    {
        if (statements.Count == 0) return;
        await using var conn = await _connections.CreateOpenConnectionAsync(connectionProfileId, ct).ConfigureAwait(false);
        await conn.ChangeDatabaseAsync(database, ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var sql in statements)
            {
                await using var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = 120 };
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { /* connection may be broken */ }
            throw;
        }
    }

    private static string? Str(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
