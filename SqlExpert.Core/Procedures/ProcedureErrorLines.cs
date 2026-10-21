using SqlExpert.Core.SqlEditor;

namespace SqlExpert.Core.Procedures;

/// <summary>Maps compile errors (line inside the compiled script) to lines of the procedure editor.</summary>
public static class ProcedureErrorLines
{
    /// <summary>
    /// Body editor line of a script line; null when the error is in the header (preamble/parameters/WITH/AS).
    /// <paramref name="bodyStartLine"/> is <see cref="ProcedureScriptBuilder.BodyStartLine"/>.
    /// </summary>
    public static int? ToBodyLine(int scriptLine, int bodyStartLine)
    {
        if (scriptLine <= 0 || bodyStartLine <= 0) return null;
        var line = scriptLine - bodyStartLine + 1;
        return line >= 1 ? line : null;
    }

    /// <summary>Line of an error message inside the executed text (server line even when attributed to the procedure).</summary>
    public static int? ScriptLine(SqlMessage message) => message.TextLine ?? message.EditorLine;

    /// <summary>
    /// Rewrites the error messages of a compile so their editor line points into the body editor
    /// (header errors point to line 1 and say so). Non-error messages are returned unchanged.
    /// </summary>
    public static IReadOnlyList<SqlMessage> MapToBody(IEnumerable<SqlMessage> messages, int bodyStartLine)
    {
        var list = new List<SqlMessage>();
        foreach (var m in messages)
        {
            if (m.Kind != SqlMessageKind.Error || ScriptLine(m) is not { } scriptLine)
            {
                list.Add(m);
                continue;
            }
            var body = ToBodyLine(scriptLine, bodyStartLine);
            var note = body is { } b
                ? $"[corpo, linha {b}]"
                : "[cabeçalho (parâmetros / WITH): confira a grade de parâmetros]";
            list.Add(m with { Text = note + " " + m.Text, EditorLine = body ?? 1 });
        }
        return list;
    }

    /// <summary>Full-text mode: the editor holds the whole script, so the line is the script line.</summary>
    public static IReadOnlyList<SqlMessage> MapToFullText(IEnumerable<SqlMessage> messages) =>
        messages.Select(m => m.Kind == SqlMessageKind.Error && ScriptLine(m) is { } l ? m with { EditorLine = l } : m).ToList();
}
