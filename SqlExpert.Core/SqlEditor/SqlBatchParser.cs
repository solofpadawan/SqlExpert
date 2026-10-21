using System.Text;
using System.Text.RegularExpressions;

namespace SqlExpert.Core.SqlEditor;

/// <summary>One batch of a script: text sent to the server, its first line in the script (1-based) and the GO repeat count.</summary>
public sealed record SqlBatch(string Text, int StartLine, int RepeatCount = 1);

/// <summary>
/// Splits T-SQL scripts on the batch separator <c>GO</c> (alone on its line, optional repeat count <c>GO 5</c>
/// and trailing <c>--</c> comment), like SSMS/sqlcmd. A <c>GO</c> inside a block comment or a multi-line
/// string/bracket identifier is not a separator. Each batch keeps the script line it starts on so server
/// error line numbers can be mapped back to the editor.
/// </summary>
public static class SqlBatchParser
{
    private static readonly Regex GoLine = new(@"^\s*GO(?:\s+(?<n>\d+))?\s*(?:--.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<SqlBatch> Split(string? script)
    {
        var result = new List<SqlBatch>();
        if (string.IsNullOrEmpty(script)) return result;

        var lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var state = new ScanState();
        var current = new List<string>();
        var currentStart = 1;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (state.IsNormal && GoLine.Match(line) is { Success: true } m)
            {
                var repeat = 1;
                if (m.Groups["n"].Success && int.TryParse(m.Groups["n"].Value, out var n) && n > 0) repeat = n;
                Flush(result, current, currentStart, repeat);
                current.Clear();
                currentStart = i + 2;
                continue;
            }

            current.Add(line);
            state.ScanLine(line);
        }

        Flush(result, current, currentStart, 1);
        return result;
    }

    private static void Flush(List<SqlBatch> result, List<string> lines, int startLine, int repeat)
    {
        var first = 0;
        while (first < lines.Count && string.IsNullOrWhiteSpace(lines[first])) first++;
        var last = lines.Count - 1;
        while (last >= first && string.IsNullOrWhiteSpace(lines[last])) last--;
        if (first > last) return;

        var sb = new StringBuilder();
        for (var i = first; i <= last; i++)
        {
            if (i > first) sb.Append("\r\n");
            sb.Append(i == last ? lines[i].TrimEnd() : lines[i]);
        }
        result.Add(new SqlBatch(sb.ToString(), startLine + first, repeat));
    }

    /// <summary>Lexical state carried across lines: nested block comments, '…' / "…" strings and […] identifiers.</summary>
    internal sealed class ScanState
    {
        private int _commentDepth;
        private char _open; // '\'' '"' '[' or '\0'

        public bool IsNormal => _commentDepth == 0 && _open == '\0';

        public void ScanLine(string line)
        {
            var i = 0;
            while (i < line.Length)
            {
                var c = line[i];
                var next = i + 1 < line.Length ? line[i + 1] : '\0';
                if (_commentDepth > 0)
                {
                    if (c == '*' && next == '/') { _commentDepth--; i += 2; continue; }
                    if (c == '/' && next == '*') { _commentDepth++; i += 2; continue; }
                    i++;
                    continue;
                }

                if (_open != '\0')
                {
                    var close = _open == '[' ? ']' : _open;
                    if (c == close)
                    {
                        if (next == close) { i += 2; continue; } // escaped '' "" ]]
                        _open = '\0';
                    }
                    i++;
                    continue;
                }

                if (c == '-' && next == '-') return; // line comment
                if (c == '/' && next == '*') { _commentDepth = 1; i += 2; continue; }
                if (c is '\'' or '"' or '[') _open = c;
                i++;
            }
        }
    }
}
