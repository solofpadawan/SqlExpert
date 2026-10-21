namespace SqlExpert.Core.Models;

/// <summary>
/// Registered database connection ("Database Registration").
/// Secrets must NOT be stored in plaintext in git; use Windows Credential Manager / DPAPI in v1.
/// New properties must have backward-compatible defaults: old connections.json files simply lack them
/// (see <see cref="Services.ConnectionProfileMigration"/>).
/// </summary>
public sealed class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Database Alias shown in the Database Explorer.</summary>
    public string DisplayName { get; set; } = "";
    public string Server { get; set; } = "localhost";
    public string? Instance { get; set; }
    public int Port { get; set; } = 1433;
    public string InitialCatalog { get; set; } = "master";
    public AuthMode AuthMode { get; set; } = AuthMode.Windows;
    public string? UserName { get; set; }
    /// <summary>Credential key / vault id — never the raw password in project files.</summary>
    public string? PasswordSecretId { get; set; }
    public bool TrustServerCertificate { get; set; }
    public bool Encrypt { get; set; } = true;
    public int ConnectTimeoutSeconds { get; set; } = 15;

    // ===== Database Properties (v2) =====

    /// <summary>Format version of this registration (0 = written before the Database Properties dialog).</summary>
    public int SettingsVersion { get; set; }

    /// <summary>Local / Remote server (null in old files: inferred from <see cref="Server"/> on load).</summary>
    public ServerLocation? Location { get; set; }

    /// <summary>Network library forced through the data source prefix (tcp:, np:, lpc:).</summary>
    public NetworkProtocol Protocol { get; set; } = NetworkProtocol.Default;

    /// <summary>Last server version seen by Test Connect / Connect, e.g. "SQL Server 2022 (16.0.1000.6) Express".</summary>
    public string? ServerVersion { get; set; }

    /// <summary>Extra connection-string keywords, one "key=value" per line (validated, no passwords).</summary>
    public string? AdditionalParameters { get; set; }

    /// <summary>DB Explorer: list master/model/msdb/tempdb under this registration.</summary>
    public bool ShowSystemDatabases { get; set; } = true;

    /// <summary>DB Explorer: tab selected when a table is opened.</summary>
    public DefaultObjectTab DefaultTableTab { get; set; } = DefaultObjectTab.Fields;

    /// <summary>SQL Editor: Autocommit state of new editors on this registration.</summary>
    public bool SqlEditorAutocommit { get; set; } = true;

    /// <summary>SQL Editor: "first N rows" of new editors (null = global setting).</summary>
    public int? SqlEditorFetchRows { get; set; }

    /// <summary>Scripts: executed right after connecting.</summary>
    public string? AfterConnectScript { get; set; }

    /// <summary>Scripts: executed right before disconnecting.</summary>
    public string? BeforeDisconnectScript { get; set; }

    /// <summary>Color Mark ("#RRGGBB", null = none): explorer marker, editor strip and status bar.</summary>
    public string? ColorMark { get; set; }

    /// <summary>Field-by-field copy (same Id).</summary>
    public ConnectionProfile Clone() => (ConnectionProfile)MemberwiseClone();

    /// <summary>
    /// True when <paramref name="other"/> points to the same target
    /// (server + instance + port + database + auth + user). Case-insensitive; display name is ignored.
    /// </summary>
    public bool HasSameTarget(ConnectionProfile other)
    {
        ArgumentNullException.ThrowIfNull(other);
        static string N(string? s) => (s ?? "").Trim();
        var cmp = StringComparer.OrdinalIgnoreCase;
        return cmp.Equals(N(Server), N(other.Server))
            && cmp.Equals(N(Instance), N(other.Instance))
            && Port == other.Port
            && cmp.Equals(N(InitialCatalog), N(other.InitialCatalog))
            && AuthMode == other.AuthMode
            && (AuthMode == AuthMode.Windows || cmp.Equals(N(UserName), N(other.UserName)));
    }

    /// <summary>
    /// True when everything that goes into the connection string is the same (target, protocol,
    /// encryption, timeout, extra parameters). Alias, scripts, colors and editor defaults are ignored.
    /// </summary>
    public bool HasSameConnectionSettings(ConnectionProfile other)
    {
        ArgumentNullException.ThrowIfNull(other);
        static string N(string? s) => (s ?? "").Trim();
        return HasSameTarget(other)
            && Protocol == other.Protocol
            && Encrypt == other.Encrypt
            && TrustServerCertificate == other.TrustServerCertificate
            && ConnectTimeoutSeconds == other.ConnectTimeoutSeconds
            && string.Equals(N(UserName), N(other.UserName), StringComparison.Ordinal)
            && string.Equals(N(AdditionalParameters).Replace("\r\n", "\n"), N(other.AdditionalParameters).Replace("\r\n", "\n"), StringComparison.Ordinal);
    }
}

public enum AuthMode
{
    Windows,
    SqlLogin,
    AzureAd
}

public enum ServerLocation
{
    Local,
    Remote
}

/// <summary>SQL Server network library (data source prefix).</summary>
public enum NetworkProtocol
{
    Default,
    Tcp,
    NamedPipes,
    SharedMemory
}

/// <summary>Tab shown first when a table editor opens.</summary>
public enum DefaultObjectTab
{
    Fields,
    Data,
    Ddl
}
