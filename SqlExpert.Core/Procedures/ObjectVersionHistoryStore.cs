using System.Globalization;
using System.Text;

namespace SqlExpert.Core.Procedures;

/// <summary>One saved definition of the Version History tab.</summary>
/// <param name="Kind">"anterior" (definition before the compile) or "nova" (compiled definition).</param>
public sealed record ObjectVersionEntry(DateTime Timestamp, string Kind, string Path, long Size)
{
    public string TimestampText => Timestamp.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
    public string KindText => Kind == ObjectVersionHistoryStore.BeforeKind ? "Antes do compile" : "Compilada";
}

/// <summary>
/// Local version history of object definitions:
/// %AppData%\SqlExpert\history\objects\&lt;registration&gt;\&lt;database&gt;\&lt;schema.object&gt;\yyyyMMdd-HHmmss-fff_{anterior|nova}.sql.
/// Every successful compile saves the previous and the new definition; the oldest compiles are pruned.
/// </summary>
public sealed class ObjectVersionHistoryStore
{
    public const string BeforeKind = "anterior";
    public const string AfterKind = "nova";
    private const string StampFormat = "yyyyMMdd-HHmmss-fff";

    public ObjectVersionHistoryStore(string? rootDirectory = null, int maxCompiles = 100)
    {
        Root = rootDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SqlExpert", "history", "objects");
        MaxCompiles = Math.Max(1, maxCompiles);
    }

    public string Root { get; }
    public int MaxCompiles { get; }

    public string DirectoryFor(string registrationId, string database, string schema, string name) =>
        Path.Combine(Root, Safe(registrationId), Safe(database), Safe(schema + "." + name));

    /// <summary>Saves the definitions of one compile (<paramref name="before"/> null for a new object). Returns the saved entries.</summary>
    public IReadOnlyList<ObjectVersionEntry> SaveCompile(string registrationId, string database, string schema, string name,
        string? before, string after, DateTime? when = null)
    {
        var dir = DirectoryFor(registrationId, database, schema, name);
        Directory.CreateDirectory(dir);
        var stamp = (when ?? DateTime.Now).ToString(StampFormat, CultureInfo.InvariantCulture);
        // Two compiles in the same millisecond: keep both.
        var baseName = stamp;
        for (var i = 1; File.Exists(Path.Combine(dir, baseName + "_" + AfterKind + ".sql")); i++)
            baseName = stamp + "." + i.ToString(CultureInfo.InvariantCulture);
        if (before is not null)
            File.WriteAllText(Path.Combine(dir, baseName + "_" + BeforeKind + ".sql"), before, new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(dir, baseName + "_" + AfterKind + ".sql"), after, new UTF8Encoding(true));
        Prune(dir);
        return List(registrationId, database, schema, name);
    }

    /// <summary>Entries of an object, newest first (the "nova" file before the "anterior" one of the same compile).</summary>
    public IReadOnlyList<ObjectVersionEntry> List(string registrationId, string database, string schema, string name)
    {
        var dir = DirectoryFor(registrationId, database, schema, name);
        if (!Directory.Exists(dir)) return Array.Empty<ObjectVersionEntry>();
        var list = new List<(string Stamp, ObjectVersionEntry Entry)>();
        foreach (var f in Directory.GetFiles(dir, "*.sql"))
        {
            var file = Path.GetFileNameWithoutExtension(f);
            var sep = file.LastIndexOf('_');
            if (sep <= 0) continue;
            var stamp = file[..sep];
            var kind = file[(sep + 1)..];
            if (kind is not (BeforeKind or AfterKind)) continue;
            var core = stamp.Split('.')[0];
            if (!DateTime.TryParseExact(core, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts)) continue;
            list.Add((stamp, new ObjectVersionEntry(ts, kind, f, new FileInfo(f).Length)));
        }
        return list
            .OrderByDescending(x => x.Entry.Timestamp)
            .ThenByDescending(x => x.Stamp, StringComparer.Ordinal)
            .ThenBy(x => x.Entry.Kind == AfterKind ? 0 : 1)
            .Select(x => x.Entry)
            .ToList();
    }

    public static string Read(ObjectVersionEntry entry) => File.ReadAllText(entry.Path);

    private void Prune(string dir)
    {
        var groups = Directory.GetFiles(dir, "*.sql")
            .GroupBy(f => { var n = Path.GetFileNameWithoutExtension(f); var i = n.LastIndexOf('_'); return i > 0 ? n[..i] : n; })
            .OrderByDescending(g => g.Key, StringComparer.Ordinal)
            .ToList();
        foreach (var g in groups.Skip(MaxCompiles))
            foreach (var f in g)
                try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string Safe(string part)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in part ?? "")
            sb.Append(invalid.Contains(c) || c is '\\' or '/' or ':' ? '_' : c);
        var s = sb.ToString().Trim().TrimEnd('.');
        return s.Length == 0 ? "_" : s;
    }
}
