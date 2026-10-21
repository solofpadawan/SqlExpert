using SqlExpert.Core.Models;

namespace SqlExpert.Core.Abstractions;

/// <summary>Reads a table's full structure and applies schema changes (Fields tab).</summary>
public interface ITableStructureService
{
    /// <summary>Null when the table does not exist.</summary>
    Task<TableStructure?> LoadAsync(string connectionProfileId, string database, string schema, string table, CancellationToken ct = default);

    /// <summary>Database default collation first, then every server collation (sys.fn_helpcollations).</summary>
    Task<IReadOnlyList<string>> GetCollationsAsync(string connectionProfileId, string database, CancellationToken ct = default);

    /// <summary>Runs the statements in order inside one transaction (all or nothing).</summary>
    Task ExecuteInTransactionAsync(string connectionProfileId, string database, IReadOnlyList<string> statements, CancellationToken ct = default);
}
