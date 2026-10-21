namespace SqlExpert.Core.SqlEditor;

/// <summary>Maps a server error line (relative to the batch) to the editor line.</summary>
public static class SqlErrorLineMapper
{
    /// <param name="textStartLine">Editor line where the executed text starts (selection start, or 1).</param>
    /// <param name="batchStartLine">Line of the batch inside the executed text (1-based, <see cref="SqlBatch.StartLine"/>).</param>
    /// <param name="serverLine">SqlError.LineNumber (1-based inside the batch; 0 = unknown).</param>
    /// <param name="procedure">SqlError.Procedure: when set, the line belongs to that module, so the batch start is used.</param>
    public static int ToEditorLine(int textStartLine, int batchStartLine, int serverLine, string? procedure = null)
    {
        var start = Math.Max(1, textStartLine) + Math.Max(1, batchStartLine) - 1;
        if (!string.IsNullOrEmpty(procedure) || serverLine <= 0) return start;
        return start + serverLine - 1;
    }

    /// <summary>SSMS-like header: "Msg 207, Level 16, State 1, Line 12".</summary>
    /// <remarks>For errors raised inside a module the line shown is the module's own line (as SSMS does).</remarks>
    public static string FormatHeader(int number, int severity, int state, int editorLine, string? procedure, int serverLine = 0) =>
        string.IsNullOrEmpty(procedure)
            ? $"Msg {number}, Level {severity}, State {state}, Line {editorLine}"
            : $"Msg {number}, Level {severity}, State {state}, Procedure {procedure}, Line {serverLine}";
}
