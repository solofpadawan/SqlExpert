namespace SqlExpert.Core.SqlEditor;

/// <summary>A name in the editor text that refers to a known database object (drawn underlined, double-click opens it).</summary>
public sealed record SqlObjectLink(int Start, int Length, CatalogObject Target)
{
    public int End => Start + Length;
}

/// <summary>Finds the identifiers of a SQL text that match objects of a <see cref="CompletionCatalog"/>.</summary>
public static class SqlObjectLinks
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Every one-, two- or three-part name (current database only) that resolves to a table, view, procedure,
    /// function, trigger, sequence, synonym or type. Comments, strings, variables and temp tables are skipped; a name
    /// after "alias." (a column) is not a link. Sorted by offset.
    /// </summary>
    public static List<SqlObjectLink> Find(string text, CompletionCatalog catalog, string defaultSchema = "dbo", int start = 0, int end = -1)
    {
        var links = new List<SqlObjectLink>();
        if (catalog.Objects.Count == 0 || string.IsNullOrEmpty(text)) return links;
        var tokens = SqlNameScanner.Tokenize(text, start, end);
        foreach (var n in SqlNameScanner.Names(tokens))
        {
            if (n.Parts.Count > 3) continue;
            var name = n.Last;
            if (name.Length == 0 || name.StartsWith('#')) continue;
            // Cheap reject before resolving: most identifiers are keywords/columns.
            if (catalog.Find(name).Count == 0) continue;
            CatalogObject? target = n.Parts.Count switch
            {
                1 => catalog.Resolve(name, null, defaultSchema),
                2 => catalog.Resolve(name, n.Parts[0], defaultSchema),
                _ => Ci.Equals(n.Parts[0], catalog.Database) ? catalog.Resolve(name, n.Parts[1], defaultSchema) : null
            };
            if (target is null) continue;
            // "x AS Clientes" / "@v Clientes" declarations are not references; a following "(" is fine (functions, procedures).
            if (n.TokenIndex > 0 && tokens[n.TokenIndex - 1] is { Kind: SqlTokenKind.Identifier, Quoted: false } prev && Ci.Equals(prev.Value, "AS")
                && target.Kind is not (CompletionKind.Type))
                continue;
            links.Add(new SqlObjectLink(n.Start, n.Length, target));
        }
        return links;
    }

    /// <summary>Link at <paramref name="offset"/> (inclusive start, inclusive end) in a sorted list, or null.</summary>
    public static SqlObjectLink? At(IReadOnlyList<SqlObjectLink> links, int offset)
    {
        int lo = 0, hi = links.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var l = links[mid];
            if (offset < l.Start) hi = mid - 1;
            else if (offset > l.End) lo = mid + 1;
            else return l;
        }
        return null;
    }
}
