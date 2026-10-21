using System.Text.RegularExpressions;

namespace SqlExpert.Core.SqlEditor;

/// <summary>Where Ctrl+Space was pressed: the word being typed and what is written before its dot.</summary>
/// <param name="WordStart">Offset of the partial word (the text replaced when an item is chosen).</param>
/// <param name="Prefix">Typed part of the word, without an opening bracket.</param>
/// <param name="Qualifier">Parts before the dot (e.g. ["dbo"] or ["c"] for "c.no|"); empty when none.</param>
/// <param name="Bracketed">The word starts with "[" (the inserted name keeps the brackets).</param>
public sealed record SqlCompletionContext(int WordStart, int Caret, string Prefix, IReadOnlyList<string> Qualifier, bool Bracketed)
{
    public int ReplaceLength => Caret - WordStart;
}

/// <summary>A table (or view / TVF) referenced in FROM / JOIN / UPDATE / INTO / MERGE, with its alias.</summary>
public sealed record SqlTableRef(string? Database, string? Schema, string Name, string? Alias, int Offset);

/// <summary>One line of the completion popup.</summary>
public sealed record SqlCompletionItem(CompletionKind Kind, string DisplayName, string InsertText, CatalogObject? Object = null, string? Detail = null)
{
    public string TypeText => Kind == CompletionKind.Field ? "Field" : Kind.ToString();
}

/// <summary>Code completion (Ctrl+Space) of the SQL Editor: context at the caret, table aliases and the item list.</summary>
public static class SqlCompletion
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    /// <summary>Most used T-SQL keywords (offered after the database objects).</summary>
    public static readonly IReadOnlyList<string> Keywords = new[]
    {
        "ADD", "ALL", "ALTER", "AND", "ANY", "APPLY", "AS", "ASC", "AVG", "BEGIN", "BETWEEN", "BREAK", "BY", "CASCADE", "CASE", "CAST",
        "CATCH", "CHECK", "CLOSE", "COALESCE", "COLLATE", "COLUMN", "COMMIT", "CONSTRAINT", "CONTINUE", "CONVERT", "COUNT", "CREATE",
        "CROSS", "CURSOR", "DEALLOCATE", "DECLARE", "DEFAULT", "DELETE", "DESC", "DISTINCT", "DROP", "ELSE", "END", "EXCEPT", "EXEC",
        "EXECUTE", "EXISTS", "FETCH", "FOR", "FOREIGN", "FROM", "FULL", "FUNCTION", "GO", "GOTO", "GRANT", "GROUP", "HAVING", "IDENTITY",
        "IF", "IIF", "IN", "INDEX", "INNER", "INSERT", "INTERSECT", "INTO", "IS", "ISNULL", "JOIN", "KEY", "LEFT", "LIKE", "MAX", "MERGE",
        "MIN", "NEXT", "NOCOUNT", "NOLOCK", "NOT", "NULL", "NULLIF", "OFFSET", "ON", "OPEN", "OPTION", "OR", "ORDER", "OUTER", "OUTPUT",
        "OVER", "PARTITION", "PRIMARY", "PRINT", "PROCEDURE", "RAISERROR", "REFERENCES", "RETURN", "RETURNS", "RIGHT", "ROLLBACK",
        "ROW_NUMBER", "ROWS", "SCHEMA", "SELECT", "SEQUENCE", "SET", "SUM", "TABLE", "THEN", "THROW", "TOP", "TRAN", "TRANSACTION",
        "TRIGGER", "TRUNCATE", "TRY", "UNION", "UNIQUE", "UPDATE", "USE", "USING", "VALUES", "VIEW", "WHEN", "WHERE", "WHILE", "WITH"
    };

    private static readonly HashSet<string> Reserved = new(Keywords.Concat(new[]
    {
        "BACKUP", "BROWSE", "BULK", "CLUSTERED", "COMPUTE", "CONTAINS", "CURRENT", "DATABASE", "DBCC", "DENY", "DISK", "DUMP", "ERRLVL",
        "ESCAPE", "EXIT", "FILE", "FILLFACTOR", "FREETEXT", "HOLDLOCK", "KILL", "LINENO", "LOAD", "NATIONAL", "NONCLUSTERED", "OF", "OFF",
        "OFFSETS", "OPENQUERY", "OPENROWSET", "PERCENT", "PIVOT", "PLAN", "PROC", "PUBLIC", "READ", "RESTORE", "RESTRICT", "REVERT",
        "REVOKE", "ROWCOUNT", "RULE", "SAVE", "SESSION_USER", "SHUTDOWN", "SOME", "STATISTICS", "SYSTEM_USER", "TEXTSIZE", "TO", "UNPIVOT",
        "USER", "VARYING", "WAITFOR", "WRITETEXT"
    }), Ci);

    /// <summary>Words that end a table reference: they are never an alias.</summary>
    private static readonly HashSet<string> NotAlias = new(Ci)
    {
        "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER", "ON", "ORDER", "GROUP", "HAVING", "UNION", "EXCEPT",
        "INTERSECT", "WITH", "SET", "SELECT", "FROM", "INTO", "VALUES", "OUTPUT", "OPTION", "FOR", "WHEN", "THEN", "ELSE", "END",
        "APPLY", "PIVOT", "UNPIVOT", "AS", "GO", "INSERT", "UPDATE", "DELETE", "MERGE", "USING", "BEGIN", "IF", "WHILE", "RETURN",
        "DECLARE", "EXEC", "EXECUTE", "PRINT", "TABLESAMPLE", "LIMIT", "OFFSET", "FETCH", "DEFAULT", "NOT", "AND", "OR"
    };

    private static readonly Regex PlainIdentifier = new(@"^[\p{L}_#][\p{L}\p{Nd}_#$@]*$", RegexOptions.Compiled);

    /// <summary>Name as it must be written: plain when possible, else [bracketed].</summary>
    public static string Quote(string name) =>
        PlainIdentifier.IsMatch(name) && !Reserved.Contains(name) ? name : "[" + name.Replace("]", "]]") + "]";

    /// <summary>Context at <paramref name="caret"/>, or null inside a comment / string literal / after a variable.</summary>
    public static SqlCompletionContext? Analyze(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var state = SqlNameScanner.StateAt(text, caret);
        if (state is SqlNameScanner.LexState.LineComment or SqlNameScanner.LexState.BlockComment or SqlNameScanner.LexState.String)
            return null;

        int wordStart;
        bool bracketed;
        if (state is SqlNameScanner.LexState.Bracket or SqlNameScanner.LexState.DoubleQuote)
        {
            var open = state == SqlNameScanner.LexState.Bracket ? '[' : '"';
            wordStart = text.LastIndexOf(open, Math.Max(0, caret - 1));
            if (wordStart < 0) return null;
            bracketed = true;
        }
        else
        {
            wordStart = caret;
            while (wordStart > 0 && SqlNameScanner.IsIdentPart(text[wordStart - 1])) wordStart--;
            if (wordStart < caret && text[wordStart] == '@') return null; // variables are not completed
            if (wordStart > 0 && text[wordStart - 1] == '@') return null;
            bracketed = false;
        }
        var prefix = bracketed ? text[(wordStart + 1)..caret] : text[wordStart..caret];
        if (!bracketed && prefix.Length > 0 && char.IsDigit(prefix[0])) return null;

        // Qualifier: identifiers separated by dots right before the word (dbo.|, c.|, [My Schema].Tab|).
        var qualifier = new List<string>();
        var p = wordStart;
        while (p > 0 && text[p - 1] == '.')
        {
            var endPart = p - 1;
            if (endPart > 0 && text[endPart - 1] == ']')
            {
                var openAt = text.LastIndexOf('[', endPart - 1);
                if (openAt < 0) break;
                qualifier.Insert(0, text[(openAt + 1)..(endPart - 1)].Replace("]]", "]"));
                p = openAt;
            }
            else
            {
                var s = endPart;
                while (s > 0 && SqlNameScanner.IsIdentPart(text[s - 1])) s--;
                if (s == endPart) break;
                if (text[s] == '@') return null; // @table.column
                qualifier.Insert(0, text[s..endPart]);
                p = s;
            }
        }
        return new SqlCompletionContext(wordStart, caret, prefix, qualifier, bracketed);
    }

    /// <summary>Bounds of the statement around <paramref name="offset"/>: between ';' / GO lines (approximate).</summary>
    public static (int Start, int End) StatementBounds(string text, IReadOnlyList<SqlToken> tokens, int offset)
    {
        var start = 0;
        var end = text.Length;
        foreach (var t in tokens)
        {
            var boundary = t.Kind == SqlTokenKind.Semicolon
                           || (t.Kind == SqlTokenKind.Identifier && !t.Quoted && Ci.Equals(t.Value, "GO") && IsAloneOnLine(text, t));
            if (!boundary) continue;
            if (t.End <= offset) start = t.End;
            else if (t.Start >= offset) { end = t.Start; break; }
        }
        return (start, end);
    }

    private static bool IsAloneOnLine(string text, SqlToken t)
    {
        var ls = text.LastIndexOf('\n', Math.Max(0, t.Start - 1)) + 1;
        if (t.Start == 0) ls = 0;
        for (var i = ls; i < t.Start; i++) if (!char.IsWhiteSpace(text[i])) return false;
        return true;
    }

    /// <summary>Tables referenced by FROM / JOIN / UPDATE / INTO / MERGE / USING, with their aliases.</summary>
    public static List<SqlTableRef> TableReferences(IReadOnlyList<SqlToken> tokens)
    {
        var refs = new List<SqlTableRef>();
        // FROM a, b JOIN c ON … , d: a comma at the FROM's own parenthesis depth starts another table until the clause ends.
        var depth = 0;
        var fromDepth = -1;
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == SqlTokenKind.OpenParen) { depth++; continue; }
            if (t.Kind == SqlTokenKind.CloseParen)
            {
                depth--;
                if (fromDepth > depth) fromDepth = -1;
                continue;
            }
            if (t.Kind == SqlTokenKind.Semicolon) { fromDepth = -1; continue; }
            if (t.Kind == SqlTokenKind.Comma && fromDepth == depth)
            {
                var (cr, cnext) = ReadTableRef(tokens, i + 1);
                if (cr is not null) refs.Add(cr);
                i = Math.Max(i, cnext - 1);
                continue;
            }
            if (t.Kind != SqlTokenKind.Identifier || t.Quoted) continue;
            var kw = t.Value.ToUpperInvariant();
            if (fromDepth == depth && kw is "WHERE" or "GROUP" or "ORDER" or "HAVING" or "UNION" or "EXCEPT" or "INTERSECT"
                    or "SELECT" or "SET" or "OPTION" or "FOR" or "WINDOW" or "INSERT" or "UPDATE" or "DELETE" or "GO")
                fromDepth = -1;
            if (kw is not ("FROM" or "JOIN" or "UPDATE" or "INTO" or "MERGE" or "USING" or "APPLY" or "TABLE")) continue;
            if (kw == "FROM") fromDepth = depth;
            var (tr, next) = ReadTableRef(tokens, i + 1);
            if (tr is not null) refs.Add(tr);
            i = Math.Max(i, next - 1);
        }
        return refs;
    }

    private static (SqlTableRef? Ref, int Next) ReadTableRef(IReadOnlyList<SqlToken> tokens, int j)
    {
        if (j >= tokens.Count || tokens[j].Kind != SqlTokenKind.Identifier) return (null, j);
        if (!tokens[j].Quoted && NotAlias.Contains(tokens[j].Value)) return (null, j);
        var parts = new List<string> { tokens[j].Value };
        var offset = tokens[j].Start;
        j++;
        while (j + 1 < tokens.Count && tokens[j].Kind == SqlTokenKind.Dot)
        {
            if (tokens[j + 1].Kind == SqlTokenKind.Dot) { parts.Add(""); j++; continue; } // db..table
            if (tokens[j + 1].Kind != SqlTokenKind.Identifier) break;
            parts.Add(tokens[j + 1].Value);
            j += 2;
        }
        // Table-valued function: skip its argument list.
        if (j < tokens.Count && tokens[j].Kind == SqlTokenKind.OpenParen)
        {
            var depth = 0;
            for (; j < tokens.Count; j++)
            {
                if (tokens[j].Kind == SqlTokenKind.OpenParen) depth++;
                else if (tokens[j].Kind == SqlTokenKind.CloseParen && --depth == 0) { j++; break; }
            }
        }
        string? alias = null;
        if (j < tokens.Count && tokens[j].Kind == SqlTokenKind.Identifier && !tokens[j].Quoted && Ci.Equals(tokens[j].Value, "AS")) j++;
        if (j < tokens.Count && tokens[j].Kind == SqlTokenKind.Identifier && (tokens[j].Quoted || !NotAlias.Contains(tokens[j].Value)))
        {
            alias = tokens[j].Value;
            j++;
        }
        var name = parts[^1];
        string? schema = parts.Count >= 2 && parts[^2].Length > 0 ? parts[^2] : null;
        string? db = parts.Count >= 3 ? parts[^3] : null;
        if (name.StartsWith('#') || name.Length == 0) return (null, j);
        return (new SqlTableRef(db, schema, name, alias, offset), j);
    }

    /// <summary>
    /// Items for <paramref name="ctx"/>: objects whose name starts with the prefix (case-insensitive), the columns of
    /// the table behind an alias / table name before the dot, objects of a schema after "schema.", then keywords.
    /// </summary>
    public static List<SqlCompletionItem> GetItems(SqlCompletionContext ctx, CompletionCatalog catalog, IReadOnlyList<SqlTableRef> refs,
        string defaultSchema = "dbo", int maxItems = 5000)
    {
        var items = new List<SqlCompletionItem>();
        var prefix = ctx.Prefix;
        bool Match(string name) => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        string Insert(string name) => ctx.Bracketed ? "[" + name.Replace("]", "]]") + "]" : Quote(name);

        var q = ctx.Qualifier;
        if (q.Count == 0)
        {
            foreach (var o in catalog.Objects)
            {
                if (o.Kind == CompletionKind.Trigger && prefix.Length == 0) continue; // triggers are not referenced in queries
                if (!Match(o.Name)) continue;
                var unique = catalog.Find(o.Name).Count == 1;
                var bare = Ci.Equals(o.Schema, defaultSchema) || unique;
                var display = Ci.Equals(o.Schema, defaultSchema) ? o.Name : $"{o.Schema}.{o.Name}";
                var insert = bare ? Insert(o.Name) : (ctx.Bracketed ? Insert(o.Name) : Quote(o.Schema) + "." + Quote(o.Name));
                items.Add(new SqlCompletionItem(o.Kind, display, insert, o));
            }
            foreach (var s in catalog.Schemas.Where(Match).OrderBy(s => s, Ci))
                items.Add(new SqlCompletionItem(CompletionKind.Schema, s, Insert(s)));
            Sort(items);
            if (!ctx.Bracketed)
                foreach (var k in Keywords.Where(Match))
                    items.Add(new SqlCompletionItem(CompletionKind.Keyword, k, k));
        }
        else
        {
            var columns = ResolveColumnsOwner(ctx, catalog, refs, defaultSchema);
            if (columns is { } owner)
            {
                foreach (var c in catalog.ColumnsOf(owner.Schema, owner.Name))
                    if (Match(c.Name))
                        items.Add(new SqlCompletionItem(CompletionKind.Field, c.Name, Insert(c.Name), owner, c.TypeText));
            }
            // schema.| (or db.schema.|): objects of that schema.
            var schema = q.Count == 1 ? q[0] : q.Count == 2 && Ci.Equals(q[0], catalog.Database) ? q[1] : null;
            if (schema is not null && catalog.IsSchema(schema))
            {
                var objs = new List<SqlCompletionItem>();
                foreach (var o in catalog.Objects)
                    if (Ci.Equals(o.Schema, schema) && Match(o.Name))
                        objs.Add(new SqlCompletionItem(o.Kind, o.Name, Insert(o.Name), o));
                Sort(objs);
                items.AddRange(objs);
            }
        }
        if (items.Count > maxItems) items.RemoveRange(maxItems, items.Count - maxItems);
        return items;
    }

    private static void Sort(List<SqlCompletionItem> items) =>
        items.Sort((a, b) =>
        {
            var c = CompletionCatalog.KindRank(a.Kind).CompareTo(CompletionCatalog.KindRank(b.Kind));
            return c != 0 ? c : Ci.Compare(a.DisplayName, b.DisplayName);
        });

    /// <summary>Table/view whose columns are wanted after "alias." / "table." / "schema.table.".</summary>
    public static CatalogObject? ResolveColumnsOwner(SqlCompletionContext ctx, CompletionCatalog catalog, IReadOnlyList<SqlTableRef> refs, string defaultSchema = "dbo")
    {
        var q = ctx.Qualifier;
        if (q.Count == 0) return null;
        bool HasColumns(CatalogObject o) => catalog.ColumnsOf(o.Schema, o.Name).Count > 0;
        if (q.Count == 1)
        {
            // Alias wins over a table name; the nearest definition before the caret wins.
            var byAlias = refs.Where(r => r.Alias is not null && Ci.Equals(r.Alias, q[0]))
                              .OrderBy(r => r.Offset <= ctx.WordStart ? ctx.WordStart - r.Offset : int.MaxValue / 2 + r.Offset)
                              .FirstOrDefault();
            var tr = byAlias ?? refs.FirstOrDefault(r => r.Alias is null && Ci.Equals(r.Name, q[0]))
                               ?? refs.FirstOrDefault(r => Ci.Equals(r.Name, q[0]));
            if (tr is not null)
            {
                var o = catalog.Resolve(tr.Name, tr.Schema, defaultSchema);
                if (o is not null && HasColumns(o)) return o;
            }
            var direct = catalog.Resolve(q[0], null, defaultSchema);
            return direct is not null && HasColumns(direct) ? direct : null;
        }
        var obj = catalog.Resolve(q[^1], q[^2], defaultSchema);
        if (q.Count >= 3 && !Ci.Equals(q[^3], catalog.Database)) return null;
        return obj is not null && HasColumns(obj) ? obj : null;
    }

    /// <summary>Convenience: context + table references of the statement + items, in one call.</summary>
    public static (SqlCompletionContext? Context, List<SqlCompletionItem> Items) Complete(string text, int caret, CompletionCatalog catalog, string defaultSchema = "dbo")
    {
        var ctx = Analyze(text, caret);
        if (ctx is null) return (null, new List<SqlCompletionItem>());
        var tokens = SqlNameScanner.Tokenize(text);
        var (s, e) = StatementBounds(text, tokens, ctx.WordStart);
        var stmtTokens = tokens.Where(t => t.Start >= s && t.End <= e).ToList();
        var refs = TableReferences(stmtTokens);
        return (ctx, GetItems(ctx, catalog, refs, defaultSchema));
    }
}
