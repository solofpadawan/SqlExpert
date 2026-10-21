using SqlExpert.Core.Models;

namespace SqlExpert.Core.SqlEditor;

/// <summary>Kind of a completion entry (also the text of the popup's Type column).</summary>
public enum CompletionKind
{
    Table,
    View,
    Procedure,
    Function,
    Trigger,
    Sequence,
    Synonym,
    Type,
    Schema,
    Field,
    Keyword
}

/// <summary>A database object known to the SQL Editor (code completion and object links).</summary>
public sealed record CatalogObject(string Schema, string Name, CompletionKind Kind)
{
    /// <summary>Explorer kind of the object (null for kinds the Explorer does not list).</summary>
    public ObjectKind? ObjectKind => Kind switch
    {
        CompletionKind.Table => Models.ObjectKind.Table,
        CompletionKind.View => Models.ObjectKind.View,
        CompletionKind.Procedure => Models.ObjectKind.Procedure,
        CompletionKind.Function => Models.ObjectKind.Function,
        CompletionKind.Trigger => Models.ObjectKind.Trigger,
        CompletionKind.Sequence => Models.ObjectKind.Sequence,
        CompletionKind.Synonym => Models.ObjectKind.Synonym,
        CompletionKind.Type => Models.ObjectKind.UserDefinedType,
        _ => null
    };

    public string QualifiedName => $"{Schema}.{Name}";
}

/// <summary>A column of a table, view or table-valued function.</summary>
public sealed record CatalogColumn(string Schema, string Table, string Name, string TypeText, int Ordinal, bool IsNullable = true);

/// <summary>
/// Object names and columns of one database, read once per connection/database and kept until the Explorer is
/// refreshed. Every lookup is case-insensitive.
/// </summary>
public sealed class CompletionCatalog
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private readonly Dictionary<string, List<CatalogObject>> _byName = new(Ci);
    private readonly Dictionary<string, List<CatalogColumn>> _columns = new(Ci);
    private readonly HashSet<string> _schemas = new(Ci);

    public CompletionCatalog(string database, IEnumerable<CatalogObject> objects, IEnumerable<CatalogColumn>? columns = null, IEnumerable<string>? schemas = null)
    {
        Database = database;
        var list = new List<CatalogObject>();
        foreach (var o in objects)
        {
            list.Add(o);
            if (!_byName.TryGetValue(o.Name, out var l)) _byName[o.Name] = l = new List<CatalogObject>(1);
            l.Add(o);
            _schemas.Add(o.Schema);
        }
        list.Sort((a, b) =>
        {
            var c = Ci.Compare(a.Name, b.Name);
            return c != 0 ? c : Ci.Compare(a.Schema, b.Schema);
        });
        Objects = list;
        if (schemas is not null)
            foreach (var s in schemas) _schemas.Add(s);
        if (columns is not null)
        {
            foreach (var c in columns)
            {
                var key = Key(c.Schema, c.Table);
                if (!_columns.TryGetValue(key, out var l)) _columns[key] = l = new List<CatalogColumn>();
                l.Add(c);
            }
            foreach (var l in _columns.Values) l.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal));
        }
    }

    public static CompletionCatalog Empty(string database) => new(database, Array.Empty<CatalogObject>());

    public string Database { get; }

    /// <summary>Every object, sorted by name then schema.</summary>
    public IReadOnlyList<CatalogObject> Objects { get; }

    public IReadOnlyCollection<string> Schemas => _schemas;

    public DateTimeOffset LoadedAt { get; init; } = DateTimeOffset.Now;

    public bool IsSchema(string name) => _schemas.Contains(name);

    private static string Key(string schema, string name) => schema + "\u0001" + name;

    /// <summary>Objects called <paramref name="name"/> (any schema when <paramref name="schema"/> is null).</summary>
    public IReadOnlyList<CatalogObject> Find(string name, string? schema = null)
    {
        if (!_byName.TryGetValue(name, out var l)) return Array.Empty<CatalogObject>();
        return schema is null ? l : l.Where(o => Ci.Equals(o.Schema, schema)).ToList();
    }

    /// <summary>
    /// The object a one- or two-part name refers to: with a schema, that exact object; without one, the object in
    /// <paramref name="defaultSchema"/>, else the only object with that name. Tables/views win over triggers etc.
    /// </summary>
    public CatalogObject? Resolve(string name, string? schema, string defaultSchema = "dbo")
    {
        if (!_byName.TryGetValue(name, out var l) || l.Count == 0) return null;
        IEnumerable<CatalogObject> candidates = l;
        if (schema is not null)
        {
            candidates = l.Where(o => Ci.Equals(o.Schema, schema));
        }
        else if (l.Count > 1)
        {
            var inDefault = l.Where(o => Ci.Equals(o.Schema, defaultSchema)).ToList();
            if (inDefault.Count > 0) candidates = inDefault;
            else if (l.Select(o => o.Schema).Distinct(Ci).Count() > 1) return null; // ambiguous without a schema
        }
        return candidates.OrderBy(o => KindRank(o.Kind)).FirstOrDefault();
    }

    /// <summary>Columns of the table/view <paramref name="schema"/>.<paramref name="table"/> (ordinal order).</summary>
    public IReadOnlyList<CatalogColumn> ColumnsOf(string schema, string table) =>
        _columns.TryGetValue(Key(schema, table), out var l) ? l : Array.Empty<CatalogColumn>();

    public static int KindRank(CompletionKind kind) => kind switch
    {
        CompletionKind.Field => 0,
        CompletionKind.Table => 1,
        CompletionKind.View => 2,
        CompletionKind.Procedure => 3,
        CompletionKind.Function => 4,
        CompletionKind.Synonym => 5,
        CompletionKind.Sequence => 6,
        CompletionKind.Type => 7,
        CompletionKind.Trigger => 8,
        CompletionKind.Schema => 9,
        _ => 10
    };
}
