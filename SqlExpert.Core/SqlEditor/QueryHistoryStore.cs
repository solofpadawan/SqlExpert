using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SqlExpert.Core.SqlEditor;

/// <summary>One executed statement in the SQL Editor history.</summary>
public sealed record QueryHistoryEntry(string Sql, DateTimeOffset ExecutedAt, string? Database, long ElapsedMs, bool Success)
{
    /// <summary>First non-empty line, trimmed to 120 characters (list caption).</summary>
    public string Preview => QueryHistoryStore.PreviewOf(Sql);
    public string ExecutedAtText => ExecutedAt.ToLocalTime().ToString("g");
    public string ElapsedText => ElapsedMs < 1000 ? $"{ElapsedMs} ms" : $"{ElapsedMs / 1000.0:0.00} s";
    public string StatusText => Success ? "OK" : "Erro";
}

/// <summary>
/// Per-connection query history: the last <see cref="MaxEntries"/> executed texts, most recent first,
/// one JSON file per connection profile under %AppData%\SqlExpert\history. Re-running a text moves it to the top.
/// Corrupt/unreadable files read as empty; writes are atomic (temp file + move).
/// </summary>
public sealed class QueryHistoryStore
{
    public const int MaxEntries = 100;
    private static readonly object FileLock = new();
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public QueryHistoryStore(string? directory = null)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory = directory;
            return;
        }
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData)) appData = Path.GetTempPath();
        Directory = Path.Combine(appData, "SqlExpert", "history");
    }

    public string Directory { get; }

    /// <summary>File used for a profile (file-name-safe version of the id).</summary>
    public string PathFor(string profileId)
    {
        var safe = new string((profileId ?? "").Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        if (string.IsNullOrEmpty(safe)) safe = "default";
        return Path.Combine(Directory, safe + ".json");
    }

    public IReadOnlyList<QueryHistoryEntry> Load(string profileId)
    {
        lock (FileLock) return LoadCore(PathFor(profileId));
    }

    /// <summary>Adds an entry (blank text is ignored) and returns the updated list.</summary>
    public IReadOnlyList<QueryHistoryEntry> Add(string profileId, QueryHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var path = PathFor(profileId);
        lock (FileLock)
        {
            var list = LoadCore(path).ToList();
            var sql = entry.Sql?.Trim() ?? "";
            if (sql.Length == 0) return list;
            list.RemoveAll(e => string.Equals(e.Sql.Trim(), sql, StringComparison.Ordinal));
            list.Insert(0, entry with { Sql = sql });
            if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);
            Save(path, list);
            return list;
        }
    }

    public void Clear(string profileId)
    {
        lock (FileLock)
        {
            var path = PathFor(profileId);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public static string PreviewOf(string? sql)
    {
        var line = (sql ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return line.Length > 120 ? line[..117] + "..." : line;
    }

    private static List<QueryHistoryEntry> LoadCore(string path)
    {
        var list = new List<QueryHistoryEntry>();
        try
        {
            if (!File.Exists(path)) return list;
            if (JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) is not JsonObject root || root["entries"] is not JsonArray arr) return list;
            foreach (var item in arr.OfType<JsonObject>())
            {
                var sql = item["sql"] is JsonValue s && s.TryGetValue<string>(out var t) ? t : null;
                if (string.IsNullOrWhiteSpace(sql)) continue;
                var at = item["executedAt"] is JsonValue a && a.TryGetValue<string>(out var at1) && DateTimeOffset.TryParse(at1, out var d) ? d : DateTimeOffset.MinValue;
                var db = item["database"] is JsonValue dbv && dbv.TryGetValue<string>(out var dbs) ? dbs : null;
                var ms = item["elapsedMs"] is JsonValue m && m.TryGetValue<long>(out var msv) ? msv : 0;
                var ok = item["success"] is not JsonValue okv || !okv.TryGetValue<bool>(out var okb) || okb;
                list.Add(new QueryHistoryEntry(sql, at, db, ms, ok));
                if (list.Count >= MaxEntries) break;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            list.Clear();
        }
        return list;
    }

    private static void Save(string path, List<QueryHistoryEntry> list)
    {
        var arr = new JsonArray();
        foreach (var e in list)
        {
            arr.Add(new JsonObject
            {
                ["sql"] = e.Sql,
                ["executedAt"] = e.ExecutedAt.ToString("o"),
                ["database"] = e.Database,
                ["elapsedMs"] = e.ElapsedMs,
                ["success"] = e.Success
            });
        }
        var root = new JsonObject { ["version"] = 1, ["entries"] = arr };
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}
