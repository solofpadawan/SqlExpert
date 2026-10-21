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

public sealed record SqlResultColumn(string Name, string DataTypeName, Type ClrType)
{
    /// <summary>Size reported by the server (characters/bytes; -1 or int.MaxValue for MAX types), when known.</summary>
    public int? Size { get; init; }
    public int? Precision { get; init; }
    public int? Scale { get; init; }
    public bool? AllowDbNull { get; init; }
}

/// <summary>One result set: at most MaxRows rows are kept; <see cref="TotalRowCount"/> counts every row the server sent.</summary>
public sealed class SqlResultSet
{
    public required IReadOnlyList<SqlResultColumn> Columns { get; init; }
    public List<object?[]> Rows { get; } = new();
    public long TotalRowCount { get; set; }
    public bool HasMoreRows => TotalRowCount > Rows.Count;
    /// <summary>1-based batch number in the executed text.</summary>
    public int BatchNumber { get; init; }
    /// <summary>Statement that produced this result (with the DECLAREs before it), when it is known for sure.</summary>
    public string? StatementText { get; set; }
    /// <summary>Source of each column (sp_describe_first_result_set, browse mode); null when not described.</summary>
    public IReadOnlyList<DescribedColumn>? Described { get; set; }
    /// <summary>Why the source could not be described (shown as the read-only note).</summary>
    public string? SourceNote { get; set; }
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
    /// <summary>Collect the actual execution plans (SET STATISTICS XML) for the Plan / Performance info messages.
    /// The plan result sets are never shown as results.</summary>
    public bool CapturePlan { get; init; }
    /// <summary>Describe the source table/key of each result set after the run (editable results).</summary>
    public bool DescribeResults { get; init; }
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
    /// <summary>SHOWPLAN XML documents received (CapturePlan).</summary>
    public List<string> Showplans { get; } = new();
    /// <summary>True when the plans were requested and SET STATISTICS XML was accepted.</summary>
    public bool PlanCaptured { get; set; }
    /// <summary>Why the plan could not be captured (no SHOWPLAN permission…), or null.</summary>
    public string? PlanNote { get; set; }
    public bool HasMoreRows => ResultSets.Any(r => r.HasMoreRows);
}
