using System.Globalization;
using System.Text;

namespace SqlExpert.Core.Explorer;

/// <summary>Role of a node in the Database Explorer filter index.</summary>
public enum ExplorerFilterRole : byte
{
    /// <summary>Registration, database or category folder: matches by its caption.</summary>
    Container,
    /// <summary>Database object ("schema.name"): an unqualified query matches the name part only.</summary>
    Object,
    /// <summary>"(loading...)" placeholder: never matches, shown only under a matched ancestor.</summary>
    Placeholder
}

/// <summary>
/// One node of the flat filter index. Entries are in pre-order: a parent always comes before its
/// children (<see cref="Parent"/> is smaller than the entry's own index, -1 for roots).
/// </summary>
/// <param name="Parent">Index of the parent entry, or -1 for a root.</param>
/// <param name="Text">Caption as shown in the tree ("dbo.Clientes", "Tables", "Demo").</param>
/// <param name="NameStart">Where the object name starts inside <paramref name="Text"/> (schema.Length + 1 for objects, 0 otherwise).</param>
/// <param name="Role">How the entry takes part in matching.</param>
public readonly record struct ExplorerFilterEntry(int Parent, string Text, int NameStart, ExplorerFilterRole Role);

/// <summary>
/// Case- and accent-insensitive text folding that keeps the length (one output char per input char),
/// so match positions found in the folded text are also valid in the original caption (bold highlight).
/// </summary>
public static class ExplorerFilterText
{
    // 0 = not computed yet. Benign race: computing the same char twice gives the same value.
    private static readonly char[] FoldTable = new char[char.MaxValue + 1];

    public static char Fold(char c)
    {
        if (c < 128)
            return c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
        var f = FoldTable[c];
        if (f != '\0') return f;
        f = ComputeFold(c);
        FoldTable[c] = f;
        return f;
    }

    private static char ComputeFold(char c)
    {
        if (char.IsSurrogate(c)) return c;
        try
        {
            var d = c.ToString().Normalize(NormalizationForm.FormD);
            var b = d.Length > 0 && CharUnicodeInfo.GetUnicodeCategory(d[0]) != UnicodeCategory.NonSpacingMark ? d[0] : c;
            return char.ToLowerInvariant(b);
        }
        catch (ArgumentException)
        {
            return char.ToLowerInvariant(c);
        }
    }

    /// <summary>Folded copy of <paramref name="s"/> (same length).</summary>
    public static string Fold(string s)
    {
        var ascii = true;
        foreach (var ch in s)
        {
            if (ch >= 128 || ch is >= 'A' and <= 'Z') { ascii = false; break; }
        }
        if (ascii) return s;
        return string.Create(s.Length, s, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++) span[i] = Fold(src[i]);
        });
    }

    /// <summary>
    /// Query as typed → folded search key: trimmed, T-SQL quoting removed ("[dbo].[Cli" → "dbo.cli").
    /// Empty when there is nothing to filter by.
    /// </summary>
    public static string NormalizeQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return "";
        var sb = new StringBuilder(query.Length);
        foreach (var ch in query.Trim())
        {
            if (ch is '[' or ']' or '"') continue;
            sb.Append(Fold(ch));
        }
        return sb.ToString().Trim();
    }
}

/// <summary>Result of one filter run. Arrays are indexed like the entries of the index.</summary>
public sealed class ExplorerFilterResult
{
    internal ExplorerFilterResult(int n, string query)
    {
        Query = query;
        IsVisible = new bool[n];
        IsExpanded = new bool[n];
        IsCovered = new bool[n];
        MatchStart = new int[n];
        VisibleChildren = new int[n];
    }

    /// <summary>Folded search key that produced this result.</summary>
    public string Query { get; }
    /// <summary>Length of the matched substring (for the bold highlight).</summary>
    public int MatchLength => Query.Length;
    /// <summary>Node stays in the tree: it matches, an ancestor matches, or a descendant matches.</summary>
    public bool[] IsVisible { get; }
    /// <summary>Node has a matching descendant: auto-expanded while the filter is active.</summary>
    public bool[] IsExpanded { get; }
    /// <summary>Node itself or an ancestor matched: its whole subtree is shown unfiltered (counts stay plain).</summary>
    public bool[] IsCovered { get; }
    /// <summary>Start of the match inside the caption, or -1.</summary>
    public int[] MatchStart { get; }
    /// <summary>Visible direct children that are not placeholders (filtered folder count).</summary>
    public int[] VisibleChildren { get; }
    /// <summary>Number of nodes whose own caption matched.</summary>
    public int MatchCount { get; internal set; }
}

/// <summary>
/// Flat, immutable snapshot of the Database Explorer captions (pre-order, folded once).
/// <see cref="Match"/> is pure and thread-safe, so it runs off the UI thread.
/// </summary>
public sealed class ExplorerFilterIndex
{
    private readonly int[] _parent;
    private readonly string[] _folded;
    private readonly int[] _nameStart;
    private readonly ExplorerFilterRole[] _role;

    public ExplorerFilterIndex(IReadOnlyList<ExplorerFilterEntry> entries)
    {
        var n = entries.Count;
        _parent = new int[n];
        _folded = new string[n];
        _nameStart = new int[n];
        _role = new ExplorerFilterRole[n];
        for (var i = 0; i < n; i++)
        {
            var e = entries[i];
            if (e.Parent >= i)
                throw new ArgumentException($"Entry {i}: parent {e.Parent} must come before the child (pre-order).", nameof(entries));
            _parent[i] = e.Parent;
            _folded[i] = ExplorerFilterText.Fold(e.Text ?? "");
            _nameStart[i] = Math.Clamp(e.NameStart, 0, _folded[i].Length);
            _role[i] = e.Role;
        }
    }

    public int Count => _parent.Length;

    /// <summary>Filters the snapshot by <paramref name="query"/> (raw text as typed). Null when the query is empty.</summary>
    public ExplorerFilterResult? Match(string? query, CancellationToken ct = default)
    {
        var q = ExplorerFilterText.NormalizeQuery(query);
        if (q.Length == 0) return null;
        // "dbo.cli" searches the whole "schema.name"; "cli" searches the object name only,
        // so typing "dbo" does not light up every object of the schema.
        var qualified = q.Contains('.');
        var n = _parent.Length;
        var r = new ExplorerFilterResult(n, q);
        var self = new bool[n];
        var below = new bool[n];
        var matches = 0;

        // Pass 1 (pre-order): own match + "covered" by a matched ancestor.
        for (var i = 0; i < n; i++)
        {
            if ((i & 1023) == 0) ct.ThrowIfCancellationRequested();
            var idx = -1;
            if (_role[i] != ExplorerFilterRole.Placeholder)
            {
                var start = qualified || _role[i] != ExplorerFilterRole.Object ? 0 : _nameStart[i];
                idx = _folded[i].IndexOf(q, start, StringComparison.Ordinal);
            }
            r.MatchStart[i] = idx;
            if (idx >= 0) { self[i] = true; matches++; }
            var p = _parent[i];
            r.IsCovered[i] = self[i] || (p >= 0 && r.IsCovered[p]);
        }

        ct.ThrowIfCancellationRequested();
        // Pass 2 (reverse pre-order = children before parents): "a descendant matched".
        for (var i = n - 1; i >= 0; i--)
        {
            var p = _parent[i];
            if (p >= 0 && (self[i] || below[i])) below[p] = true;
        }

        // Pass 3: visibility, auto-expansion and filtered child counts.
        for (var i = 0; i < n; i++)
        {
            var visible = r.IsCovered[i] || below[i];
            r.IsVisible[i] = visible;
            r.IsExpanded[i] = below[i];
            var p = _parent[i];
            if (visible && p >= 0 && _role[i] != ExplorerFilterRole.Placeholder)
                r.VisibleChildren[p]++;
        }

        r.MatchCount = matches;
        return r;
    }
}
