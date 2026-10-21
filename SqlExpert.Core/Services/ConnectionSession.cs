using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>In-memory registry of connection profiles used by the factory and UI.</summary>
public sealed class ConnectionSession
{
    private readonly List<ConnectionProfile> _profiles = new();
    private readonly object _gate = new();

    public IReadOnlyList<ConnectionProfile> Profiles
    {
        get { lock (_gate) return _profiles.ToList(); }
    }

    public ConnectionProfile? Find(string id)
    {
        lock (_gate)
            return _profiles.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public void ReplaceAll(IEnumerable<ConnectionProfile> profiles)
    {
        lock (_gate)
        {
            _profiles.Clear();
            _profiles.AddRange(profiles);
        }
    }

    public void Upsert(ConnectionProfile profile)
    {
        lock (_gate)
        {
            var idx = _profiles.FindIndex(p => p.Id == profile.Id);
            if (idx >= 0) _profiles[idx] = profile;
            else _profiles.Add(profile);
        }
    }

    /// <summary>
    /// Registrations in DB Explorer order: as registered (store order) or by alias
    /// (case-insensitive, ties keep the registration order).
    /// </summary>
    public IReadOnlyList<ConnectionProfile> Ordered(bool byAlias) => SortForExplorer(Profiles, byAlias);

    public static IReadOnlyList<ConnectionProfile> SortForExplorer(IEnumerable<ConnectionProfile> profiles, bool byAlias)
    {
        var list = profiles.ToList();
        if (!byAlias) return list;
        static string Key(ConnectionProfile p) => string.IsNullOrWhiteSpace(p.DisplayName) ? $"{p.Server} / {p.InitialCatalog}" : p.DisplayName;
        return list.Select((p, i) => (p, i))
            .OrderBy(x => Key(x.p), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.i)
            .Select(x => x.p)
            .ToList();
    }

    public bool Remove(string id)
    {
        lock (_gate)
            return _profiles.RemoveAll(p => p.Id == id) > 0;
    }
}
