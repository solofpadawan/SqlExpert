using System.Collections.ObjectModel;

namespace SqlExpert.Core.Documents;

/// <summary>
/// UI-independent MDI document list (classic-style): one document per key, an active document,
/// activation of an already-open document, close / close others / cycling.
/// Confirmation (unsaved changes) is the caller's job; this class only tracks state.
/// </summary>
public sealed class DocumentManager<TDoc> where TDoc : class
{
    private readonly Func<TDoc, string> _keyOf;
    private TDoc? _active;

    public DocumentManager(Func<TDoc, string> keyOf) => _keyOf = keyOf ?? throw new ArgumentNullException(nameof(keyOf));

    public ObservableCollection<TDoc> Documents { get; } = new();

    public TDoc? Active => _active;

    /// <summary>Raised after the active document changes: (previous, current).</summary>
    public event Action<TDoc?, TDoc?>? ActiveChanged;

    public int Count => Documents.Count;

    public TDoc? Find(string key) =>
        Documents.FirstOrDefault(d => string.Equals(_keyOf(d), key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Activates the open document with <paramref name="key"/>, or creates, adds and activates a new one.</summary>
    public (TDoc Document, bool Created) OpenOrActivate(string key, Func<TDoc> factory)
    {
        var existing = Find(key);
        if (existing is not null)
        {
            Activate(existing);
            return (existing, false);
        }

        var doc = factory() ?? throw new InvalidOperationException("Document factory returned null.");
        if (!string.Equals(_keyOf(doc), key, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Document key does not match the requested key.");
        Documents.Add(doc);
        Activate(doc);
        return (doc, true);
    }

    public void Activate(TDoc? doc)
    {
        if (doc is not null && !Documents.Contains(doc))
            throw new InvalidOperationException("Document is not open.");
        if (ReferenceEquals(_active, doc)) return;
        var previous = _active;
        _active = doc;
        ActiveChanged?.Invoke(previous, doc);
    }

    /// <summary>Removes a document. If it was active, the neighbour to its left (else the new first) becomes active.</summary>
    public bool Remove(TDoc doc)
    {
        var index = Documents.IndexOf(doc);
        if (index < 0) return false;
        var wasActive = ReferenceEquals(_active, doc);
        Documents.RemoveAt(index);
        if (wasActive)
        {
            TDoc? next = Documents.Count == 0 ? null : Documents[Math.Max(0, Math.Min(index - 1, Documents.Count - 1))];
            var previous = _active;
            _active = next;
            ActiveChanged?.Invoke(previous, next);
        }
        return true;
    }

    /// <summary>Snapshot of every open document except <paramref name="keep"/>.</summary>
    public IReadOnlyList<TDoc> Others(TDoc keep) => Documents.Where(d => !ReferenceEquals(d, keep)).ToList();

    /// <summary>Snapshot of all open documents.</summary>
    public IReadOnlyList<TDoc> All() => Documents.ToList();

    /// <summary>Activates the next (<paramref name="step"/> = 1) or previous (-1) document, wrapping around.</summary>
    public TDoc? Cycle(int step)
    {
        if (Documents.Count == 0) return null;
        var index = _active is null ? -1 : Documents.IndexOf(_active);
        var count = Documents.Count;
        var next = index < 0
            ? (step >= 0 ? 0 : count - 1)
            : ((index + step) % count + count) % count;
        Activate(Documents[next]);
        return _active;
    }
}

public static class DocumentKeys
{
    /// <summary>Key of a table editor document: profile + database + schema + table (case-insensitive).</summary>
    public static string Table(string profileId, string database, string schema, string table) =>
        string.Join("|", "table", profileId, database, schema, table).ToLowerInvariant();

    /// <summary>Key of a procedure editor document: profile + database + schema + procedure (case-insensitive).</summary>
    public static string Procedure(string profileId, string database, string schema, string name) =>
        string.Join("|", "procedure", profileId, database, schema, name).ToLowerInvariant();

    /// <summary>Key of a New Procedure document (not on the server yet).</summary>
    public static string NewProcedure(string profileId, string database, int number) =>
        string.Join("|", "newprocedure", profileId, database, number.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToLowerInvariant();

    /// <summary>Key of a SQL Editor document: profile + editor number (several editors per connection).</summary>
    public static string SqlEditor(string profileId, int number) =>
        string.Join("|", "sqleditor", profileId, number.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToLowerInvariant();
}

public static class WindowTitles
{
    public const string AppName = "SqlExpert";

    /// <summary>"Table : [dbo].[Clientes]"</summary>
    public static string TableCaption(string schema, string table) => $"Table : [{schema}].[{table}]";

    /// <summary>"Procedure : [dbo].[AtualizaCodigo]"</summary>
    public static string ProcedureCaption(string schema, string name) => $"Procedure : [{schema}].[{name}]";

    /// <summary>"SQL Editor", "SQL Editor 2", … plus " - file.sql" when a file is loaded.</summary>
    public static string SqlEditorCaption(int number, string? fileName = null)
    {
        var caption = number <= 1 ? "SQL Editor" : $"SQL Editor {number}";
        return string.IsNullOrWhiteSpace(fileName) ? caption : $"{caption} - {fileName}";
    }

    /// <summary>
    /// classic-style main window title:
    /// "SqlExpert - [Table : [dbo].[Clientes]] : SQL Server (localhost\SQLEXPRESS:SqlExpertTeste)",
    /// or just "SqlExpert" when no document is active.
    /// </summary>
    public static string Build(string? documentCaption, string? profileName, string? server, string? instance, string? database)
    {
        if (string.IsNullOrWhiteSpace(documentCaption))
            return AppName;

        var title = $"{AppName} - [{documentCaption}]";
        if (!string.IsNullOrWhiteSpace(profileName))
            title += " : " + profileName;

        var source = string.IsNullOrWhiteSpace(instance) ? server : $"{server}\\{instance}";
        if (!string.IsNullOrWhiteSpace(source) || !string.IsNullOrWhiteSpace(database))
            title += $" ({source}:{database})";
        return title;
    }
}
