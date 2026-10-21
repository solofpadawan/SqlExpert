using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>
/// Brings registrations read from older connections.json files to the current shape
/// (version <see cref="CurrentVersion"/>). Idempotent; never touches secrets.
/// </summary>
public static class ConnectionProfileMigration
{
    /// <summary>2 = Database Properties dialog (location, protocol, scripts, color mark, editor defaults).</summary>
    public const int CurrentVersion = 2;

    public static ConnectionProfile Normalize(ConnectionProfile p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (string.IsNullOrWhiteSpace(p.Id)) p.Id = Guid.NewGuid().ToString("N");
        p.DisplayName ??= "";

        // A protocol prefix typed into the server (tcp:host) moves to the Protocol field.
        var server = ServerNameText.StripPrefix(p.Server, out var prefixProtocol);
        if (prefixProtocol is { } pp && p.Protocol == NetworkProtocol.Default) p.Protocol = pp;

        // "host\INSTANCE" / "host,port" typed into the server field are split.
        if (server.Contains('\\') || server.Contains(','))
        {
            try
            {
                var parts = ServerNameText.Parse(server);
                server = parts.Server;
                if (string.IsNullOrWhiteSpace(p.Instance)) p.Instance = parts.Instance;
                if (parts.Port != ServerNameText.DefaultPort) p.Port = parts.Port;
            }
            catch (FormatException)
            {
                // Keep the text as typed; connecting will report it.
            }
        }
        p.Server = string.IsNullOrWhiteSpace(server) ? "localhost" : server;
        if (string.IsNullOrWhiteSpace(p.Instance)) p.Instance = null;
        if (p.Port <= 0 || p.Port > 65535) p.Port = ServerNameText.DefaultPort;
        if (string.IsNullOrWhiteSpace(p.InitialCatalog)) p.InitialCatalog = "master";
        if (p.ConnectTimeoutSeconds <= 0) p.ConnectTimeoutSeconds = 15;

        p.Location ??= ServerNameText.IsLocalHost(p.Server) ? ServerLocation.Local : ServerLocation.Remote;
        if (p.SqlEditorFetchRows is { } rows) p.SqlEditorFetchRows = UserSettings.ClampFetchRows(rows);
        p.ColorMark = ColorMarks.Normalize(p.ColorMark);
        if (string.IsNullOrWhiteSpace(p.AdditionalParameters)) p.AdditionalParameters = null;
        if (string.IsNullOrWhiteSpace(p.AfterConnectScript)) p.AfterConnectScript = null;
        if (string.IsNullOrWhiteSpace(p.BeforeDisconnectScript)) p.BeforeDisconnectScript = null;
        if (p.AuthMode != AuthMode.SqlLogin) p.UserName = string.IsNullOrWhiteSpace(p.UserName) ? null : p.UserName;
        if (p.SettingsVersion < CurrentVersion) p.SettingsVersion = CurrentVersion;
        return p;
    }
}
