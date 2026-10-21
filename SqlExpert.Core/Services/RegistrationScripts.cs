using Microsoft.Data.SqlClient;
using SqlExpert.Core.SqlEditor;

namespace SqlExpert.Core.Services;

/// <summary>
/// Scripts of a registration (After Connect / Before Disconnect) and the server probes used by the
/// Database Properties dialog (server version, database list).
/// </summary>
public static class RegistrationScripts
{
    public static bool HasScript(string? script) => SqlBatchParser.Split(script).Any(b => !string.IsNullOrWhiteSpace(b.Text));

    /// <summary>
    /// Runs every GO batch of <paramref name="script"/> on <paramref name="connection"/>. Stops at the first error.
    /// Never throws for SQL errors: returns the error messages (empty = success).
    /// </summary>
    public static async Task<IReadOnlyList<string>> RunAsync(SqlConnection connection, string? script, CancellationToken ct = default)
    {
        var errors = new List<string>();
        foreach (var batch in SqlBatchParser.Split(script))
        {
            if (string.IsNullOrWhiteSpace(batch.Text)) continue;
            for (var i = 0; i < Math.Max(1, batch.RepeatCount); i++)
            {
                try
                {
                    await using var cmd = new SqlCommand(batch.Text, connection) { CommandTimeout = 60 };
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                catch (SqlException ex)
                {
                    var line = ex.LineNumber > 0 ? batch.StartLine + ex.LineNumber - 1 : batch.StartLine;
                    errors.Add($"Linha {line}: {ex.Message}");
                    return errors;
                }
                catch (InvalidOperationException ex)
                {
                    errors.Add(ex.Message);
                    return errors;
                }
            }
        }
        return errors;
    }

    public static async Task<string> GetServerVersionAsync(SqlConnection connection, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand(ServerVersionText.Query, connection);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return "SQL Server";
        return ServerVersionText.Format(
            r.IsDBNull(0) ? null : r.GetString(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? null : r.GetInt32(2));
    }

    public static async Task<IReadOnlyList<string>> ListDatabasesAsync(SqlConnection connection, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand(MetadataSql.ListDatabases, connection);
        var list = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(r.GetString(0));
        return list;
    }
}
