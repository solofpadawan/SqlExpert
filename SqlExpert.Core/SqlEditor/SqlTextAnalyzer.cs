using System.Text;
using System.Text.RegularExpressions;

namespace SqlExpert.Core.SqlEditor;

/// <summary>Small lexical helpers for the SQL Editor (no full T-SQL parser).</summary>
public static class SqlTextAnalyzer
{
    private static readonly HashSet<string> Modifying = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "INTO", "EXEC", "EXECUTE", "DROP", "ALTER", "CREATE", "TRUNCATE",
        "GRANT", "REVOKE", "DENY", "BACKUP", "RESTORE", "DBCC", "BEGIN", "COMMIT", "ROLLBACK", "SAVE", "USE",
        "KILL", "SHUTDOWN", "RECONFIGURE", "WRITETEXT", "UPDATETEXT", "BULK", "OPENROWSET", "OPENQUERY", "SET", "WAITFOR"
    };

    /// <summary>Replaces comments, string literals and quoted identifiers with spaces (keeps line breaks and length).</summary>
    public static string StripCommentsAndLiterals(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            if (c == '-' && next == '-')
            {
                while (i < sql.Length && sql[i] != '\n') { sb.Append(' '); i++; }
                continue;
            }
            if (c == '/' && next == '*')
            {
                var depth = 0;
                while (i < sql.Length)
                {
                    var a = sql[i];
                    var b = i + 1 < sql.Length ? sql[i + 1] : '\0';
                    if (a == '/' && b == '*') { depth++; sb.Append("  "); i += 2; continue; }
                    if (a == '*' && b == '/') { depth--; sb.Append("  "); i += 2; if (depth == 0) break; continue; }
                    sb.Append(a == '\n' ? '\n' : ' ');
                    i++;
                }
                continue;
            }
            if (c is '\'' or '"' or '[')
            {
                var close = c == '[' ? ']' : c;
                sb.Append(' ');
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == close)
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == close) { sb.Append("  "); i += 2; continue; }
                        sb.Append(' ');
                        i++;
                        break;
                    }
                    sb.Append(sql[i] == '\n' ? '\n' : ' ');
                    i++;
                }
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// True when the text looks like plain queries only (SELECT / WITH … SELECT, no INTO/DML/DDL/EXEC/SET),
    /// i.e. re-running it to fetch all rows has no side effects. Conservative: unknown → false.
    /// </summary>
    public static bool IsReadOnlyQuery(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return false;
        var words = Regex.Matches(StripCommentsAndLiterals(sql), @"[A-Za-z_][A-Za-z0-9_@#$]*").Select(m => m.Value).ToList();
        if (words.Count == 0) return false;
        var first = words[0].ToUpperInvariant();
        if (first is not ("SELECT" or "WITH" or "DECLARE")) return false;
        return !words.Any(Modifying.Contains);
    }
}
