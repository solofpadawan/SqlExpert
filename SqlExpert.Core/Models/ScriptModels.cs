namespace SqlExpert.Core.Models;

public sealed class ScriptOptions
{
    public bool StopOnError { get; set; } = true;
    public int CommandTimeoutSeconds { get; set; } = 30;
}

public sealed class ScriptProgress
{
    public int BatchIndex { get; set; }
    public int TotalBatches { get; set; }
    public string Message { get; set; } = "";
}

public sealed class ScriptResult
{
    public bool Success { get; set; }
    public IReadOnlyList<BatchResult> Batches { get; set; } = Array.Empty<BatchResult>();
    public string? ErrorMessage { get; set; }
}

public sealed class BatchResult
{
    public int Index { get; set; }
    public string Sql { get; set; } = "";
    public int RecordsAffected { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Error { get; set; }
    public IReadOnlyList<ResultSet>? ResultSets { get; set; }
}

public sealed class ResultSet
{
    public IReadOnlyList<string> ColumnNames { get; set; } = Array.Empty<string>();
    public IReadOnlyList<object?[]> Rows { get; set; } = Array.Empty<object?[]>();
}
