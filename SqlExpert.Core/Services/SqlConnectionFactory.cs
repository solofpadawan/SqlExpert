using Microsoft.Data.SqlClient;
using SqlExpert.Core.Abstractions;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly Func<string, ConnectionProfile?> _resolveProfile;
    private readonly ICredentialStore _credentials;

    public SqlConnectionFactory(
        Func<string, ConnectionProfile?> resolveProfile,
        ICredentialStore credentials)
    {
        _resolveProfile = resolveProfile;
        _credentials = credentials;
    }

    public string BuildConnectionString(ConnectionProfile profile, string? password = null)
        => BuildConnectionStringCore(profile, password);

    public async Task<SqlConnection> CreateOpenConnectionAsync(
        string connectionProfileId,
        CancellationToken cancellationToken = default)
    {
        var profile = _resolveProfile(connectionProfileId)
            ?? throw new InvalidOperationException($"Connection profile '{connectionProfileId}' not found.");
        return await OpenAsync(profile, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SqlConnection> OpenAsync(
        ConnectionProfile profile,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        if (password is null
            && profile.AuthMode == AuthMode.SqlLogin
            && !string.IsNullOrEmpty(profile.PasswordSecretId))
        {
            password = await _credentials.GetSecretAsync(profile.PasswordSecretId, cancellationToken).ConfigureAwait(false);
        }

        var conn = new SqlConnection(BuildConnectionStringCore(profile, password));
        try
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return conn;
    }

    public async Task TestConnectionAsync(
        ConnectionProfile profile,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenAsync(profile, password, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds DataSource: [protocol prefix]server\instance or [prefix]server,port
    /// (tcp: / np: / lpc: for TCP/IP, Named Pipes, Shared Memory; nothing for Default).
    /// </summary>
    public static string BuildDataSource(ConnectionProfile profile)
    {
        var prefix = ServerNameText.Prefix(profile.Protocol);
        var server = ServerNameText.StripPrefix(profile.Server, out _);
        if (server.Length == 0) server = "localhost";

        if (!string.IsNullOrWhiteSpace(profile.Instance))
            return $"{prefix}{server}\\{profile.Instance.Trim()}";

        if (profile.Port > 0 && profile.Port != ServerNameText.DefaultPort)
            return $"{prefix}{server},{profile.Port}";

        return prefix + server;
    }

    private static string BuildConnectionStringCore(ConnectionProfile profile, string? password)
    {
        var csb = new SqlConnectionStringBuilder
        {
            DataSource = BuildDataSource(profile),
            InitialCatalog = string.IsNullOrWhiteSpace(profile.InitialCatalog) ? "master" : profile.InitialCatalog,
            Encrypt = profile.Encrypt,
            TrustServerCertificate = profile.TrustServerCertificate,
            ConnectTimeout = profile.ConnectTimeoutSeconds <= 0 ? 15 : profile.ConnectTimeoutSeconds,
            ApplicationName = "SqlExpert"
        };

        // Extra keywords first; the fields owned by the dialog (auth, server, database…) are reserved anyway.
        AdditionalConnectParameters.ApplyTo(csb, profile.AdditionalParameters);

        switch (profile.AuthMode)
        {
            case AuthMode.Windows:
                csb.IntegratedSecurity = true;
                break;
            case AuthMode.SqlLogin:
                csb.UserID = profile.UserName ?? "";
                csb.Password = password ?? "";
                break;
            case AuthMode.AzureAd:
                csb.Authentication = SqlAuthenticationMethod.ActiveDirectoryInteractive;
                if (!string.IsNullOrWhiteSpace(profile.UserName)) csb.UserID = profile.UserName;
                break;
        }

        return csb.ConnectionString;
    }
}
