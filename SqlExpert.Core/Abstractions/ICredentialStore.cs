namespace SqlExpert.Core.Abstractions;

/// <summary>
/// Stores secrets (SQL login passwords) outside of connections.json.
/// Production (WPF on Windows): <c>DpapiCredentialStore</c> using Windows DPAPI
/// (<see cref="System.Security.Cryptography.ProtectedData"/>).
/// Tests: <c>InMemoryCredentialStore</c>.
/// </summary>
public interface ICredentialStore
{
    Task SetSecretAsync(string secretId, string secret, CancellationToken cancellationToken = default);
    Task<string?> GetSecretAsync(string secretId, CancellationToken cancellationToken = default);
    Task DeleteSecretAsync(string secretId, CancellationToken cancellationToken = default);
}
