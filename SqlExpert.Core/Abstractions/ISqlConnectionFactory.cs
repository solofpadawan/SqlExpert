using Microsoft.Data.SqlClient;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.Abstractions;

/// <summary>
/// Builds connection strings and opens SqlConnection instances from registered profiles.
/// </summary>
public interface ISqlConnectionFactory
{
    /// <summary>
    /// Builds a connection string. When <paramref name="password"/> is null and auth is SQL login,
    /// the password is resolved from <see cref="ICredentialStore"/> via <see cref="ConnectionProfile.PasswordSecretId"/>.
    /// </summary>
    string BuildConnectionString(ConnectionProfile profile, string? password = null);

    Task<SqlConnection> CreateOpenConnectionAsync(
        string connectionProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a connection for a profile that may not be registered yet (Database Properties dialog).
    /// When <paramref name="password"/> is null the stored secret is used.
    /// </summary>
    Task<SqlConnection> OpenAsync(
        ConnectionProfile profile,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Opens and immediately closes a connection to verify credentials/network.</summary>
    Task TestConnectionAsync(
        ConnectionProfile profile,
        string? password = null,
        CancellationToken cancellationToken = default);
}
