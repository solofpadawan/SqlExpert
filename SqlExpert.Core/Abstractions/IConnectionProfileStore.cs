using SqlExpert.Core.Models;

namespace SqlExpert.Core.Abstractions;

/// <summary>
/// Persists registered connection profiles (no passwords) as JSON.
/// Default path: %AppData%/SqlExpert/connections.json
/// </summary>
public interface IConnectionProfileStore
{
    Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IEnumerable<ConnectionProfile> profiles, CancellationToken cancellationToken = default);
    string StorePath { get; }
}
