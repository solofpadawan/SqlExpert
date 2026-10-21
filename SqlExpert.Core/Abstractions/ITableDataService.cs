using SqlExpert.Core.Models;

namespace SqlExpert.Core.Abstractions;

/// <summary>Reads and writes table rows for the object editor's Data tab.</summary>
public interface ITableDataService
{
    Task<TableDataSchema> GetSchemaAsync(
        string connectionProfileId, string database, string schema, string table, CancellationToken ct = default);

    /// <summary>SELECT TOP (<paramref name="top"/>) ordered by PK when present.</summary>
    Task<TableDataPage> LoadAsync(
        string connectionProfileId, string database, TableDataSchema schema, int top, CancellationToken ct = default);

    /// <summary>
    /// Applies all changes in one transaction (all or nothing). Returns, per change, the row re-read
    /// from the server (Insert/Update) in schema column order, or null when it could not be re-read (or Delete).
    /// </summary>
    Task<IReadOnlyList<object[]?>> ApplyChangesAsync(
        string connectionProfileId, string database, TableDataSchema schema, IReadOnlyList<RowChange> changes, CancellationToken ct = default);

    /// <summary>Foreign keys of the table (sys.foreign_keys / sys.foreign_key_columns), grouped per constraint.</summary>
    Task<IReadOnlyList<ForeignKeyInfo>> GetForeignKeysAsync(
        string connectionProfileId, string database, string schema, string table, CancellationToken ct = default);

    /// <summary>
    /// Rows of the table referenced by <paramref name="fk"/> for the lookup popup: key columns + display columns,
    /// SELECT TOP (<paramref name="top"/>) ordered by the key (<paramref name="top"/> &lt;= 0 = all rows). When
    /// <paramref name="currentKey"/> is not among them, that row is read by key and appended. Read-only.
    /// </summary>
    Task<LookupData> GetLookupDataAsync(
        string connectionProfileId, string database, ForeignKeyInfo fk, int top, IReadOnlyList<object?>? currentKey, CancellationToken ct = default);
}
