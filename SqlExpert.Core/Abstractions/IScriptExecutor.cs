using SqlExpert.Core.Models;

namespace SqlExpert.Core.Abstractions;

/// <summary>
/// Executes .sql scripts with GO batch separators (IBEScript-like Script Executive).
/// </summary>
public interface IScriptExecutor
{
    Task<ScriptResult> ExecuteAsync(
        string connectionProfileId,
        string database,
        string scriptText,
        ScriptOptions? options = null,
        CancellationToken cancellationToken = default,
        IProgress<ScriptProgress>? progress = null);
}
