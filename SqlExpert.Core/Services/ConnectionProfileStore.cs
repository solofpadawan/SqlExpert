using System.Text.Json;
using System.Text.Json.Serialization;
using SqlExpert.Core.Abstractions;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>
/// Persists <see cref="ConnectionProfile"/> list as JSON.
/// Never writes passwords — only <see cref="ConnectionProfile.PasswordSecretId"/>.
/// </summary>
public sealed class ConnectionProfileStore : IConnectionProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;

    public ConnectionProfileStore(string? storePath = null)
    {
        if (!string.IsNullOrWhiteSpace(storePath))
        {
            _path = storePath;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
                appData = Path.Combine(Path.GetTempPath(), "SqlExpert");
            var dir = Path.Combine(appData, "SqlExpert");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "connections.json");
        }
    }

    public string StorePath => _path;

    public async Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return Array.Empty<ConnectionProfile>();

        await using var stream = File.OpenRead(_path);
        var list = await JsonSerializer.DeserializeAsync<List<ConnectionProfile>>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (list is null) return Array.Empty<ConnectionProfile>();
        // Older files (no protocol/location/scripts/…) are upgraded in memory; written back on the next save.
        return list.Where(p => p is not null).Select(ConnectionProfileMigration.Normalize).ToList();
    }

    public async Task SaveAsync(IEnumerable<ConnectionProfile> profiles, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Defensive: write the known shape only (a copy of each registration; never a password).
        var safe = profiles.Select(p =>
        {
            var copy = p.Clone();
            copy.SettingsVersion = ConnectionProfileMigration.CurrentVersion;
            return copy;
        }).ToList();

        // Write to a temp file first so a crash never leaves a half-written connections.json.
        var tmp = _path + ".tmp";
        await using (var tmpStream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(tmpStream, safe, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        File.Move(tmp, _path, overwrite: true);
    }
}
