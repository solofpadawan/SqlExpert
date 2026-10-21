using System.Globalization;

namespace SqlExpert.Core.SqlEditor;

/// <summary>Status-line texts of the SQL Editor (pt-BR wording, culture-aware numbers).</summary>
public static class SqlEditorStatus
{
    public static string FormatElapsed(TimeSpan elapsed, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (elapsed.TotalSeconds < 60) return elapsed.TotalSeconds.ToString("0.000", culture) + " s";
        return $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}.{elapsed.Milliseconds / 100} min";
    }

    /// <summary>E.g. "Concluído em 0,123 s · 1.000 registro(s) (há mais — Fetch all) · 3 linha(s) afetada(s)".</summary>
    public static string Summarize(SqlExecutionResult result, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var parts = new List<string>();
        var time = FormatElapsed(result.Elapsed, culture);
        if (result.Cancelled) parts.Add($"Cancelado após {time}");
        else if (result.HasErrors) parts.Add(result.FirstErrorLine is { } line ? $"Erro (linha {line}) após {time}" : $"Erro após {time}");
        else parts.Add($"Concluído em {time}");

        if (result.ResultSets.Count > 0)
        {
            var rows = result.RowsFetched.ToString("N0", culture);
            var text = $"{rows} registro(s)";
            if (result.ResultSets.Count > 1) text += $" em {result.ResultSets.Count} resultados";
            if (result.HasMoreRows) text += " (há mais — Fetch all)";
            parts.Add(text);
        }
        if (result.RowsAffected >= 0)
            parts.Add($"{result.RowsAffected.ToString("N0", culture)} linha(s) afetada(s)");
        if (result.BatchCount > 1)
            parts.Add($"{result.BatchesExecuted}/{result.BatchCount} lotes");
        return string.Join(" · ", parts);
    }

    /// <summary>Tab header of a result set: "Result 2 (1000+)".</summary>
    public static string ResultHeader(int index, SqlResultSet set, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var count = set.Rows.Count.ToString("N0", culture) + (set.HasMoreRows ? "+" : "");
        return $"Result {index} ({count})";
    }

    private static readonly System.Text.RegularExpressions.Regex SchemaChange = new(
        @"\b(CREATE|ALTER|DROP)\s+(TABLE|VIEW|PROC|PROCEDURE|FUNCTION|TRIGGER|SEQUENCE|SYNONYM|TYPE|SCHEMA)\b(?!\s+(IF\s+EXISTS\s+)?#)|\bsp_rename\b|\bCREATE\s+OR\s+ALTER\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>True when the executed text creates/changes/drops objects (code completion reads the catalog again).</summary>
    public static bool ChangesSchema(string? sql) =>
        !string.IsNullOrEmpty(sql) && SchemaChange.IsMatch(SqlTextAnalyzer.StripCommentsAndLiterals(sql));

    public static string TransactionText(bool autocommit, int tranCount) =>
        tranCount > 0
            ? $"Transação ABERTA (@@TRANCOUNT = {tranCount})"
            : autocommit ? "Autocommit" : "Transação explícita (abre na próxima execução)";
}
