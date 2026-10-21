using System.Text.RegularExpressions;
using SqlExpert.Core.Abstractions;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>
/// Splits scripts on GO (line-alone) like IBEScript / SSMS. Execution against SQL Server is stubbed.
/// </summary>
public sealed class ScriptExecutor : IScriptExecutor
{
    private readonly ISqlConnectionFactory _connections;

    public ScriptExecutor(ISqlConnectionFactory connections) => _connections = connections;

    public static IReadOnlyList<string> SplitBatches(string scriptText)
    {
        // GO on its own line (case-insensitive), SSMS/IBEScript style; shared with the SQL Editor.
        return SqlExpert.Core.SqlEditor.SqlBatchParser.Split(scriptText).Select(b => b.Text).ToList();
    }

    public Task<ScriptResult> ExecuteAsync(
        string connectionProfileId,
        string database,
        string scriptText,
        ScriptOptions? options = null,
        CancellationToken cancellationToken = default,
        IProgress<ScriptProgress>? progress = null)
    {
        var batches = SplitBatches(scriptText);
        var results = new List<BatchResult>();
        for (var i = 0; i < batches.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScriptProgress
            {
                BatchIndex = i + 1,
                TotalBatches = batches.Count,
                Message = $"Batch {i + 1}/{batches.Count} (stub — not executed)"
            });
            results.Add(new BatchResult { Index = i, Sql = batches[i], RecordsAffected = -1 });
        }

        return Task.FromResult(new ScriptResult { Success = true, Batches = results });
    }
}
