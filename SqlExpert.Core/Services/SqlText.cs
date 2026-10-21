namespace SqlExpert.Core.Services;

/// <summary>T-SQL quoting helpers (identifiers with [] escaping, Unicode string literals).</summary>
public static class SqlText
{
    /// <summary>[name] with ']' doubled.</summary>
    public static string Bracket(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    /// <summary>[schema].[name]</summary>
    public static string Qualified(string schema, string name) => Bracket(schema) + "." + Bracket(name);

    /// <summary>N'text' with quotes doubled.</summary>
    public static string NLiteral(string text) => "N'" + text.Replace("'", "''") + "'";

    /// <summary>Strips balanced outer parentheses and whitespace: "((0))" → "0".</summary>
    public static string StripOuterParens(string? expr)
    {
        var s = (expr ?? "").Trim();
        while (s.Length >= 2 && s[0] == '(' && s[^1] == ')' && IsWrappedByOnePair(s))
            s = s[1..^1].Trim();
        return s;
    }

    private static bool IsWrappedByOnePair(string s)
    {
        var depth = 0;
        var inString = false;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == '\'') { inString = !inString; continue; }
            if (inString) continue;
            if (ch == '(') depth++;
            else if (ch == ')')
            {
                depth--;
                if (depth == 0 && i < s.Length - 1) return false;
            }
        }
        return depth == 0;
    }
}
