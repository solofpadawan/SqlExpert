using SqlExpert.Core.Models;

namespace SqlExpert.Core.Abstractions;

/// <summary>
/// Loads SQL Server catalog metadata for the Database Explorer and object editors.
/// </summary>
public interface IMetadataService
{
    Task<IReadOnlyList<string>> ListDatabasesAsync(string connectionProfileId, CancellationToken ct = default);

    /// <summary>
    /// Category folders with object counts (Tables (n), Views (n), …). Children are not populated;
    /// call <see cref="ListObjectsAsync"/> when expanding a category.
    /// </summary>
    Task<IReadOnlyList<SchemaNode>> GetObjectTreeAsync(string connectionProfileId, string database, CancellationToken ct = default);

    /// <summary>Lists objects of a given kind inside a database (lazy-load for Explorer).</summary>
    Task<IReadOnlyList<ObjectRef>> ListObjectsAsync(
        string connectionProfileId,
        string database,
        ObjectKind kind,
        CancellationToken ct = default);

    Task<TableMetadata?> GetTableAsync(
        string connectionProfileId,
        string database,
        string schema,
        string table,
        CancellationToken ct = default);

    Task<string> ScriptObjectDdlAsync(string connectionProfileId, ObjectRef objectRef, CancellationToken ct = default);
}
