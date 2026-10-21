using System.Collections.Concurrent;
using SqlExpert.Core.Abstractions;

namespace SqlExpert.Core.Services;

/// <summary>In-memory credential store for unit tests and non-Windows hosts.</summary>
public sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public Task SetSecretAsync(string secretId, string secret, CancellationToken cancellationToken = default)
    {
        _secrets[secretId] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecretAsync(string secretId, CancellationToken cancellationToken = default)
    {
        _secrets.TryGetValue(secretId, out var value);
        return Task.FromResult<string?>(value);
    }

    public Task DeleteSecretAsync(string secretId, CancellationToken cancellationToken = default)
    {
        _secrets.TryRemove(secretId, out _);
        return Task.CompletedTask;
    }
}
