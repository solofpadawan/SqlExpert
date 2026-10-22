using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlExpert.Core.Documents;

/// <summary>Kind of a saved document tab.</summary>
public enum SessionTabKind
{
    Table,
    Procedure,
    SqlEditor
}

/// <summary>One document tab of the Windows bar as saved for a database registration (no credentials).</summary>
public sealed class SessionTab
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SessionTabKind Kind { get; set; }

    /// <summary>Database of the object (SQL Editor: its current database).</summary>
    public string Database { get; set; } = "";

    /// <summary>Schema of a table / procedure.</summary>
    public string? Schema { get; set; }

    /// <summary>Name of a table / procedure.</summary>
    public string? Name { get; set; }

    /// <summary>Active page of the editor (table: Fields / Data / DDL …; procedure: Edit / Results …).</summary>
    public int SelectedTab { get; set; }

    /// <summary>SQL Editor number ("SQL Editor 2" = 2).</summary>
    public int Number { get; set; }

    /// <summary>SQL Editor text (also when it was never saved to a file).</summary>
    public string? SqlText { get; set; }

    /// <summary>SQL Editor file, when the text was loaded from / saved to one.</summary>
    public string? FilePath { get; set; }

    /// <summary>SQL Editor text differs from its file (or there is no file).</summary>
    public bool IsModified { get; set; }

    /// <summary>Identity used to drop duplicates: same rules as the document keys (case-insensitive).</summary>
    [JsonIgnore]
    public string Identity => Kind switch
    {
        SessionTabKind.Table => string.Join("|", "table", Database, Schema, Name).ToLowerInvariant(),
        SessionTabKind.Procedure => string.Join("|", "procedure", Database, Schema, Name).ToLowerInvariant(),
        _ => "sqleditor|" + Number.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    /// <summary>"[dbo].[Clientes] (db)" / "SQL Editor 2" for logs and status messages.</summary>
    [JsonIgnore]
    public string DisplayText => Kind == SessionTabKind.SqlEditor
        ? WindowTitles.SqlEditorCaption(Number)
        : $"{Kind} [{Schema}].[{Name}] ({Database})";
}

/// <summary>Open document tabs of one database registration, in Windows-bar order, plus the active one.</summary>
public sealed class OpenTabsSession
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Registration (profile) id the tabs belong to.</summary>
    public string ProfileId { get; set; } = "";

    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;

    public List<SessionTab> Tabs { get; set; } = new();

    /// <summary>Index in <see cref="Tabs"/> of the active tab, -1 = none.</summary>
    public int ActiveIndex { get; set; } = -1;
}

/// <summary>
/// Validation of a loaded session before it is reopened: drops entries that cannot be restored
/// (unknown kind, missing database / schema / name, duplicates) and keeps the active tab pointing at the same entry.
/// </summary>
public static class OpenTabsRestorePlan
{
    /// <summary>Largest SQL text kept per editor (characters); longer texts are not saved.</summary>
    public const int MaxSqlTextLength = 4 * 1024 * 1024;

    public static OpenTabsSession Normalize(OpenTabsSession? session, Action<string>? log = null)
    {
        var result = new OpenTabsSession { ProfileId = session?.ProfileId ?? "", SavedAt = session?.SavedAt ?? DateTimeOffset.Now };
        if (session?.Tabs is null) return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < session.Tabs.Count; i++)
        {
            var tab = session.Tabs[i];
            if (tab is null) continue;
            string? problem = null;
            if (!Enum.IsDefined(tab.Kind)) problem = "tipo desconhecido";
            else if (tab.Kind != SessionTabKind.SqlEditor
                     && (string.IsNullOrWhiteSpace(tab.Database) || string.IsNullOrWhiteSpace(tab.Schema) || string.IsNullOrWhiteSpace(tab.Name)))
                problem = "database/schema/nome ausente";
            else if (tab.Kind == SessionTabKind.SqlEditor && tab.Number < 1) problem = "número de SQL Editor inválido";
            else if (!seen.Add(tab.Identity)) problem = "duplicada";

            if (problem is not null)
            {
                log?.Invoke($"Aba {i + 1} ignorada ({problem}).");
                continue;
            }

            if (tab.SelectedTab < 0) tab.SelectedTab = 0;
            if (tab.SqlText is { Length: > MaxSqlTextLength }) tab.SqlText = null;
            if (i == session.ActiveIndex) result.ActiveIndex = result.Tabs.Count;
            result.Tabs.Add(tab);
        }
        return result;
    }
}

/// <summary>
/// Saved open tabs, one JSON file per registration under %AppData%\SqlExpert\sessions (file name = registration id).
/// Missing or corrupt files give no session; writes go through a temporary file. Passwords are never part of a session.
/// </summary>
public sealed class OpenTabsSessionStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private const long MaxLogBytes = 256 * 1024;

    public OpenTabsSessionStore(string? directory = null)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory = directory;
            return;
        }
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.GetTempPath();
        Directory = Path.Combine(appData, "SqlExpert", "sessions");
    }

    public string Directory { get; }

    /// <summary>Restore notes (skipped objects, unreadable files): sessions\session.log.</summary>
    public string LogPath => Path.Combine(Directory, "session.log");

    public string PathFor(string profileId) => Path.Combine(Directory, FileNameFor(profileId));

    /// <summary>Registration ids are hex GUIDs; anything else is hashed so it is always a safe file name.</summary>
    public static string FileNameFor(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var safe = profileId.Length <= 64 && profileId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        var name = safe
            ? profileId.ToLowerInvariant()
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileId))).ToLowerInvariant()[..32];
        return name + ".json";
    }

    /// <summary>The saved session of a registration, or null when there is none or the file cannot be read.</summary>
    public OpenTabsSession? Load(string profileId)
    {
        var path = PathFor(profileId);
        try
        {
            if (!File.Exists(path)) return null;
            var session = JsonSerializer.Deserialize<OpenTabsSession>(File.ReadAllText(path, Encoding.UTF8), Options);
            if (session is null) return null;
            if (!string.IsNullOrEmpty(session.ProfileId) && !string.Equals(session.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                Log($"{Path.GetFileName(path)}: pertence a outro registro; ignorado.");
                return null;
            }
            session.ProfileId = profileId;
            return session;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Log($"{Path.GetFileName(path)}: arquivo ilegível ou corrompido, ignorado ({ex.GetType().Name}).");
            return null;
        }
    }

    /// <summary>Saves the session; an empty one removes the file (nothing to reopen).</summary>
    public void Save(OpenTabsSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.ProfileId);
        if (session.Tabs.Count == 0)
        {
            Delete(session.ProfileId);
            return;
        }
        session.Version = OpenTabsSession.CurrentVersion;
        if (session.ActiveIndex < -1 || session.ActiveIndex >= session.Tabs.Count) session.ActiveIndex = -1;
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(session.ProfileId);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(session, Options), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    public void Delete(string profileId)
    {
        var path = PathFor(profileId);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Appends a timestamped line to <see cref="LogPath"/>; never throws.</summary>
    public void Log(string message)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > MaxLogBytes) info.Delete();
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  {message}{Environment.NewLine}", new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging is best effort.
        }
    }
}

/// <summary>Checks which saved tables / procedures still exist before their tabs are reopened.</summary>
public static class OpenTabsExistence
{
    /// <summary>Type of the object named by @schema/@name in the current database ('U', 'V', 'P', …) or NULL.</summary>
    public const string ObjectTypeSql =
        "SELECT o.type FROM sys.objects o WHERE o.object_id = OBJECT_ID(QUOTENAME(@schema) + N'.' + QUOTENAME(@name));";

    public static bool Matches(SessionTabKind kind, string? objectType) => kind switch
    {
        SessionTabKind.Table => objectType is "U",
        SessionTabKind.Procedure => objectType is "P" or "PC",
        _ => true
    };

    /// <summary>
    /// Returns the tabs that can be reopened: SQL Editors always; tables / procedures only when the object still exists.
    /// <paramref name="openDatabase"/> gives an open connection on a database (it may throw when the database is gone:
    /// every tab of that database is then skipped). Skipped tabs are reported through <paramref name="log"/>.
    /// </summary>
    public static async Task<IReadOnlyList<SessionTab>> FilterExistingAsync(
        IReadOnlyList<SessionTab> tabs, Func<string, CancellationToken, Task<DbConnection>> openDatabase,
        Action<string>? log = null, CancellationToken ct = default)
    {
        var keep = new HashSet<SessionTab>(ReferenceEqualityComparer.Instance);
        foreach (var tab in tabs.Where(t => t.Kind == SessionTabKind.SqlEditor)) keep.Add(tab);

        foreach (var group in tabs.Where(t => t.Kind != SessionTabKind.SqlEditor)
                     .GroupBy(t => t.Database, StringComparer.OrdinalIgnoreCase))
        {
            DbConnection? conn = null;
            try
            {
                conn = await openDatabase(group.Key, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is DbException or InvalidOperationException)
            {
                foreach (var t in group) log?.Invoke($"{t.DisplayText}: database indisponível, aba não reaberta ({ex.Message}).");
                continue;
            }

            await using (conn)
            {
                foreach (var tab in group)
                {
                    string? type;
                    try
                    {
                        type = await ObjectTypeAsync(conn, tab.Schema!, tab.Name!, ct).ConfigureAwait(false);
                    }
                    catch (DbException ex)
                    {
                        log?.Invoke($"{tab.DisplayText}: verificação falhou, aba não reaberta ({ex.Message}).");
                        continue;
                    }
                    if (Matches(tab.Kind, type)) keep.Add(tab);
                    else log?.Invoke($"{tab.DisplayText}: objeto não existe mais, aba não reaberta.");
                }
            }
        }

        return tabs.Where(keep.Contains).ToList();
    }

    private static async Task<string?> ObjectTypeAsync(DbConnection conn, string schema, string name, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ObjectTypeSql;
        AddParameter(cmd, "@schema", schema);
        AddParameter(cmd, "@name", name);
        var value = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)?.Trim();
    }

    private static void AddParameter(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = DbType.String;
        p.Size = 128;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
