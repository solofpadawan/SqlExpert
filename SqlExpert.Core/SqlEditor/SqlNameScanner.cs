namespace SqlExpert.Core.SqlEditor;

public enum SqlTokenKind
{
    /// <summary>Plain or [bracketed] / "quoted" identifier (also keywords).</summary>
    Identifier,
    /// <summary>@variable / @@function.</summary>
    Variable,
    Dot,
    Comma,
    Semicolon,
    OpenParen,
    CloseParen,
    Number,
    /// <summary>Any other operator / punctuation.</summary>
    Other
}

/// <summary>One token; <see cref="Value"/> is the identifier without its brackets/quotes.</summary>
public readonly record struct SqlToken(SqlTokenKind Kind, int Start, int Length, string Value, bool Quoted = false)
{
    public int End => Start + Length;
}

/// <summary>A multi-part name (a.b.c) found in the text; <see cref="Parts"/> are unquoted.</summary>
public sealed record SqlNameRef(IReadOnlyList<string> Parts, int Start, int Length, int TokenIndex, int TokenCount)
{
    public int End => Start + Length;
    public string Last => Parts[^1];
}

/// <summary>
/// Lexer for the editor features (completion, object links): identifiers, variables and punctuation, skipping
/// comments and string literals. Not a parser: it never fails on incomplete text.
/// </summary>
public static class SqlNameScanner
{
    public static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '#';
    public static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '#' or '$' or '@';

    public static List<SqlToken> Tokenize(string text, int start = 0, int end = -1)
    {
        if (end < 0 || end > text.Length) end = text.Length;
        var tokens = new List<SqlToken>();
        var i = Math.Max(0, start);
        while (i < end)
        {
            var c = text[i];
            var next = i + 1 < end ? text[i + 1] : '\0';
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && next == '-')
            {
                while (i < end && text[i] != '\n') i++;
                continue;
            }
            if (c == '/' && next == '*')
            {
                var depth = 0;
                while (i < end)
                {
                    if (text[i] == '/' && i + 1 < end && text[i + 1] == '*') { depth++; i += 2; continue; }
                    if (text[i] == '*' && i + 1 < end && text[i + 1] == '/') { depth--; i += 2; if (depth == 0) break; continue; }
                    i++;
                }
                continue;
            }
            if (c == '\'' || ((c == 'N' || c == 'n') && next == '\''))
            {
                if (c != '\'') i++;
                i++;
                while (i < end)
                {
                    if (text[i] == '\'')
                    {
                        if (i + 1 < end && text[i + 1] == '\'') { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
                continue;
            }
            if (c is '[' or '"')
            {
                var close = c == '[' ? ']' : '"';
                var s = i;
                i++;
                var sb = new System.Text.StringBuilder();
                var closed = false;
                while (i < end)
                {
                    if (text[i] == close)
                    {
                        if (i + 1 < end && text[i + 1] == close) { sb.Append(close); i += 2; continue; }
                        i++;
                        closed = true;
                        break;
                    }
                    sb.Append(text[i]);
                    i++;
                }
                _ = closed;
                tokens.Add(new SqlToken(SqlTokenKind.Identifier, s, i - s, sb.ToString(), Quoted: true));
                continue;
            }
            if (c == '@')
            {
                var s = i;
                i++;
                while (i < end && IsIdentPart(text[i])) i++;
                tokens.Add(new SqlToken(SqlTokenKind.Variable, s, i - s, text[s..i]));
                continue;
            }
            if (IsIdentStart(c))
            {
                var s = i;
                i++;
                while (i < end && IsIdentPart(text[i])) i++;
                tokens.Add(new SqlToken(SqlTokenKind.Identifier, s, i - s, text[s..i]));
                continue;
            }
            if (char.IsDigit(c))
            {
                var s = i;
                while (i < end && (char.IsLetterOrDigit(text[i]) || text[i] == '.')) i++;
                tokens.Add(new SqlToken(SqlTokenKind.Number, s, i - s, text[s..i]));
                continue;
            }
            var kind = c switch
            {
                '.' => SqlTokenKind.Dot,
                ',' => SqlTokenKind.Comma,
                ';' => SqlTokenKind.Semicolon,
                '(' => SqlTokenKind.OpenParen,
                ')' => SqlTokenKind.CloseParen,
                _ => SqlTokenKind.Other
            };
            tokens.Add(new SqlToken(kind, i, 1, c.ToString()));
            i++;
        }
        return tokens;
    }

    /// <summary>Groups identifier(.identifier)* sequences into names. Names right after a variable or a dangling dot are skipped.</summary>
    public static List<SqlNameRef> Names(IReadOnlyList<SqlToken> tokens)
    {
        var names = new List<SqlNameRef>();
        var i = 0;
        while (i < tokens.Count)
        {
            if (tokens[i].Kind != SqlTokenKind.Identifier) { i++; continue; }
            // "x.y" where x is not an identifier (e.g. @t.col, 1.e) is not a name.
            if (i > 0 && tokens[i - 1].Kind == SqlTokenKind.Dot) { i++; continue; }
            var parts = new List<string> { tokens[i].Value };
            var startToken = i;
            var j = i + 1;
            while (j + 1 < tokens.Count && tokens[j].Kind == SqlTokenKind.Dot && tokens[j].Start == tokens[j - 1].End
                   && tokens[j + 1].Kind == SqlTokenKind.Identifier && tokens[j + 1].Start == tokens[j].End)
            {
                parts.Add(tokens[j + 1].Value);
                j += 2;
            }
            var first = tokens[startToken];
            var last = tokens[j - 1];
            names.Add(new SqlNameRef(parts, first.Start, last.End - first.Start, startToken, j - startToken));
            i = j;
        }
        return names;
    }

    public enum LexState { Normal, LineComment, BlockComment, String, Bracket, DoubleQuote }

    /// <summary>Lexical state at <paramref name="offset"/> (completion is not offered inside comments and strings).</summary>
    public static LexState StateAt(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var state = LexState.Normal;
        var depth = 0;
        var i = 0;
        while (i < offset)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            switch (state)
            {
                case LexState.Normal:
                    if (c == '-' && next == '-') { state = LexState.LineComment; i += 2; continue; }
                    if (c == '/' && next == '*') { state = LexState.BlockComment; depth = 1; i += 2; continue; }
                    if (c == '\'') state = LexState.String;
                    else if (c == '[') state = LexState.Bracket;
                    else if (c == '"') state = LexState.DoubleQuote;
                    break;
                case LexState.LineComment:
                    if (c == '\n') state = LexState.Normal;
                    break;
                case LexState.BlockComment:
                    if (c == '/' && next == '*') { depth++; i += 2; continue; }
                    if (c == '*' && next == '/') { depth--; i += 2; if (depth == 0) state = LexState.Normal; continue; }
                    break;
                case LexState.String:
                    if (c == '\'') { if (next == '\'' && i + 1 < offset) { i += 2; continue; } state = LexState.Normal; }
                    break;
                case LexState.Bracket:
                    if (c == ']') { if (next == ']' && i + 1 < offset) { i += 2; continue; } state = LexState.Normal; }
                    break;
                case LexState.DoubleQuote:
                    if (c == '"') { if (next == '"' && i + 1 < offset) { i += 2; continue; } state = LexState.Normal; }
                    break;
            }
            i++;
        }
        return state;
    }
}
