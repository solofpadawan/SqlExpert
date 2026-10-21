namespace SqlExpert.Core.SqlEditor;

public enum SqlMessageKind
{
    /// <summary>PRINT / RAISERROR with severity ≤ 10 / server informational message.</summary>
    Info,
    /// <summary>Server error (severity ≥ 11) or client failure.</summary>
    Error,
    /// <summary>"(N row(s) affected)".</summary>
    RowsAffected,
    /// <summary>Messages produced by SqlExpert itself (cancel, transaction state, …).</summary>
    Status
}

/// <summary>One line of the Messages tab. <see cref="EditorLine"/> is the editor line (1-based) when known.</summary>
/// <param name="TextLine">Line of the error inside the executed text, even when the server attributes it to a module
/// (e.g. a compile error of CREATE PROCEDURE); null when the server sent no line.</param>
public sealed record SqlMessage(SqlMessageKind Kind, string Text, int? EditorLine = null, int? Number = null, int? Severity = null, int? TextLine = null);

public sealed record SqlResultColumn(string Name, string DataTypeName, Type ClrType);

/// <summary>One result set: at most MaxRows rows are kept; <see cref="TotalRowCount"/> counts every row the server sent.</summary>
public sealed class SqlResultSet
{
    public required IReadOnlyList<SqlResultColumn> Columns { get; init; }
    public List<object?[]> Rows { get; } = new();
    public long TotalRowCount { get; set; }
    public bool HasMoreRows => TotalRowCount > Rows.Count;
    /// <summary>1-based batch number in the executed text.</summary>
    public int BatchNumber { get; init; }
}

public sealed class SqlExecutionRequest
{
    public required string Sql { get; init; }
    /// <summary>Editor line where <see cref="Sql"/> starts (1 when executing everything; the selection's first line otherwise).</summary>
    public int StartLine { get; init; } = 1;
    /// <summary>Rows kept per result set; 0 = fetch all.</summary>
    public int MaxRows { get; init; } = 1000;
    public bool StopOnError { get; init; } = true;
    /// <summary>0 = no timeout (cancel with Stop).</summary>
    public int CommandTimeoutSeconds { get; init; }
}

public sealed class SqlExecutionResult
{
    public List<SqlResultSet> ResultSets { get; } = new();
    public List<SqlMessage> Messages { get; } = new();
    /// <summary>Sum of "rows affected" of INSERT/UPDATE/DELETE/MERGE…; -1 when no statement reported a count.</summary>
    public long RowsAffected { get; set; } = -1;
    public TimeSpan Elapsed { get; set; }
    public bool Cancelled { get; set; }
    public bool HasErrors => Messages.Any(m => m.Kind == SqlMessageKind.Error);
    public int? FirstErrorLine => Messages.FirstOrDefault(m => m.Kind == SqlMessageKind.Error && m.EditorLine is not null)?.EditorLine;
    public int BatchCount { get; set; }
    public int BatchesExecuted { get; set; }
    /// <summary>@@TRANCOUNT after the run (0 = no open transaction).</summary>
    public int TranCount { get; set; }
    public string? CurrentDatabase { get; set; }
    public long RowsFetched => ResultSets.Sum(r => (long)r.Rows.Count);
    public bool HasMoreRows => ResultSets.Any(r => r.HasMoreRows);
}
