using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.Core.Registration;

/// <summary>
/// Editable state of the "Database Properties" dialog (Database Registration Info). Bound by the WPF window;
/// validation and the copy back to <see cref="ConnectionProfile"/> live here so they are unit-tested.
/// The password is not part of the form (PasswordBox, stored with DPAPI by the caller).
/// </summary>
public sealed class RegistrationForm : INotifyPropertyChanged
{
    public const int MinTimeout = 1;
    public const int MaxTimeout = 600;

    private int _locationIndex;
    private string _serverName = "localhost";
    private int _protocolIndex;
    private string? _serverVersion;
    private string _database = "master";
    private string _alias = "";
    private int _authIndex;
    private string _userName = "";
    private bool _encrypt = true;
    private bool _trustServerCertificate;
    private string _connectTimeout = "15";
    private string _additionalParameters = "";
    private bool _showSystemDatabases = true;
    private int _defaultTableTabIndex;
    private bool _sqlEditorAutocommit = true;
    private string _sqlEditorFetchRows = "";
    private string _afterConnectScript = "";
    private string _beforeDisconnectScript = "";
    private string _colorMark = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    // ===== General =====

    /// <summary>0 = Local, 1 = Remote.</summary>
    public int LocationIndex { get => _locationIndex; set => Set(ref _locationIndex, Math.Clamp(value, 0, 1), nameof(IsRemote)); }
    public bool IsRemote => _locationIndex == 1;

    /// <summary>host, host\INSTANCE, host,port (a tcp:/np:/lpc: prefix selects the protocol).</summary>
    public string ServerName { get => _serverName; set => Set(ref _serverName, value ?? ""); }

    /// <summary>0 Default, 1 TCP/IP, 2 Named Pipes, 3 Shared Memory (same order as <see cref="NetworkProtocol"/>).</summary>
    public int ProtocolIndex { get => _protocolIndex; set => Set(ref _protocolIndex, Math.Clamp(value, 0, 3)); }

    public string? ServerVersion { get => _serverVersion; set => Set(ref _serverVersion, value); }
    public string Database { get => _database; set => Set(ref _database, value ?? ""); }
    public string Alias { get => _alias; set => Set(ref _alias, value ?? ""); }

    /// <summary>0 Windows, 1 SQL Server, 2 Microsoft Entra (interactive) (same order as <see cref="AuthMode"/>).</summary>
    public int AuthIndex
    {
        get => _authIndex;
        set => Set(ref _authIndex, Math.Clamp(value, 0, 2), nameof(IsSqlLogin), nameof(IsUserEnabled), nameof(IsWindowsAuth));
    }

    public bool IsWindowsAuth => _authIndex == 0;
    public bool IsSqlLogin => _authIndex == 1;
    public bool IsUserEnabled => _authIndex != 0;

    public string UserName { get => _userName; set => Set(ref _userName, value ?? ""); }
    public bool Encrypt { get => _encrypt; set => Set(ref _encrypt, value); }
    public bool TrustServerCertificate { get => _trustServerCertificate; set => Set(ref _trustServerCertificate, value); }
    public string ConnectTimeout { get => _connectTimeout; set => Set(ref _connectTimeout, value ?? ""); }
    public string AdditionalParameters { get => _additionalParameters; set => Set(ref _additionalParameters, value ?? ""); }

    /// <summary>Recent servers for the Server name drop-down.</summary>
    public ObservableCollection<string> RecentServers { get; } = new();
    /// <summary>Databases of the server (filled when the Database drop-down opens).</summary>
    public ObservableCollection<string> Databases { get; } = new();

    // ===== Additional =====

    public bool ShowSystemDatabases { get => _showSystemDatabases; set => Set(ref _showSystemDatabases, value); }
    /// <summary>0 Fields, 1 Data, 2 DDL.</summary>
    public int DefaultTableTabIndex { get => _defaultTableTabIndex; set => Set(ref _defaultTableTabIndex, Math.Clamp(value, 0, 2)); }
    public bool SqlEditorAutocommit { get => _sqlEditorAutocommit; set => Set(ref _sqlEditorAutocommit, value); }
    /// <summary>Blank = global setting.</summary>
    public string SqlEditorFetchRows { get => _sqlEditorFetchRows; set => Set(ref _sqlEditorFetchRows, value ?? ""); }

    // ===== Scripts / Color Mark =====

    public string AfterConnectScript { get => _afterConnectScript; set => Set(ref _afterConnectScript, value ?? ""); }
    public string BeforeDisconnectScript { get => _beforeDisconnectScript; set => Set(ref _beforeDisconnectScript, value ?? ""); }

    /// <summary>"#RRGGBB" or blank (no color).</summary>
    public string ColorMark { get => _colorMark; set => Set(ref _colorMark, value ?? "", nameof(ColorMarkPreview)); }

    /// <summary>Normalized color for previews (null when blank/invalid).</summary>
    public string? ColorMarkPreview => ColorMarks.Normalize(_colorMark);

    // ===== Load / copy / validate / apply =====

    public static RegistrationForm FromProfile(ConnectionProfile p)
    {
        var f = new RegistrationForm();
        f.Load(p, includeAlias: true);
        return f;
    }

    /// <summary>
    /// "Copy Registration Data...": takes every field of <paramref name="source"/> except the alias
    /// (and the color mark, which identifies a registration).
    /// </summary>
    public void CopyFrom(ConnectionProfile source) => Load(source, includeAlias: false);

    private void Load(ConnectionProfile p, bool includeAlias)
    {
        var location = p.Location ?? (ServerNameText.IsLocalHost(p.Server) ? ServerLocation.Local : ServerLocation.Remote);
        LocationIndex = location == ServerLocation.Remote ? 1 : 0;
        ServerName = ServerNameText.Format(p);
        ProtocolIndex = (int)p.Protocol;
        ServerVersion = p.ServerVersion;
        Database = string.IsNullOrWhiteSpace(p.InitialCatalog) ? "master" : p.InitialCatalog;
        if (includeAlias)
        {
            Alias = p.DisplayName ?? "";
            ColorMark = p.ColorMark ?? "";
        }
        AuthIndex = (int)p.AuthMode;
        UserName = p.UserName ?? "";
        Encrypt = p.Encrypt;
        TrustServerCertificate = p.TrustServerCertificate;
        ConnectTimeout = (p.ConnectTimeoutSeconds <= 0 ? 15 : p.ConnectTimeoutSeconds).ToString();
        AdditionalParameters = p.AdditionalParameters ?? "";
        ShowSystemDatabases = p.ShowSystemDatabases;
        DefaultTableTabIndex = (int)p.DefaultTableTab;
        SqlEditorAutocommit = p.SqlEditorAutocommit;
        SqlEditorFetchRows = p.SqlEditorFetchRows?.ToString() ?? "";
        AfterConnectScript = p.AfterConnectScript ?? "";
        BeforeDisconnectScript = p.BeforeDisconnectScript ?? "";
    }

    /// <summary>Problems that block OK / Test Connect (empty = valid). Each message names its page.</summary>
    public IReadOnlyList<string> Validate(bool connectionOnly = false)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ServerName))
            errors.Add("General: informe o Server name.");
        else
        {
            try
            {
                var parts = ServerNameText.Parse(ServerName);
                var protocol = parts.Protocol ?? (NetworkProtocol)ProtocolIndex;
                if (protocol == NetworkProtocol.SharedMemory && (IsRemote || !ServerNameText.IsLocalHost(parts.Server)))
                    errors.Add("General: Shared Memory só funciona com servidor Local (localhost, . ou (local)).");
            }
            catch (FormatException ex)
            {
                errors.Add("General: " + ex.Message);
            }
        }

        if (!connectionOnly && string.IsNullOrWhiteSpace(Alias))
            errors.Add("General: informe o Database Alias.");
        if (IsSqlLogin && string.IsNullOrWhiteSpace(UserName))
            errors.Add("General: informe o User Name (autenticação SQL Server).");
        if (!int.TryParse(ConnectTimeout.Trim(), out var t) || t < MinTimeout || t > MaxTimeout)
            errors.Add($"General: Connect timeout deve ser um número de {MinTimeout} a {MaxTimeout} segundos.");
        foreach (var e in AdditionalConnectParameters.Validate(AdditionalParameters))
            errors.Add("Additional connect parameters — " + e);

        if (connectionOnly) return errors;

        if (SqlEditorFetchRows.Trim().Length > 0
            && (!int.TryParse(SqlEditorFetchRows.Trim(), out var rows) || rows < 1 || rows > UserSettings.MaxSqlFetchRows))
            errors.Add($"Additional > SQL Editor: registros por consulta deve ser de 1 a {UserSettings.MaxSqlFetchRows:N0} (ou vazio).");
        if (!ColorMarks.IsValidOrEmpty(ColorMark))
            errors.Add("Color Mark: cor inválida (use #RRGGBB).");
        return errors;
    }

    /// <summary>
    /// Writes the form into <paramref name="target"/> (Id and PasswordSecretId are kept). Call <see cref="Validate"/> first;
    /// throws <see cref="FormatException"/> for an invalid server name.
    /// </summary>
    public ConnectionProfile ApplyTo(ConnectionProfile target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var parts = ServerNameText.Parse(ServerName);
        target.Location = IsRemote ? ServerLocation.Remote : ServerLocation.Local;
        target.Server = parts.Server;
        target.Instance = parts.Instance;
        target.Port = parts.Port;
        target.Protocol = parts.Protocol ?? (NetworkProtocol)ProtocolIndex;
        target.ServerVersion = string.IsNullOrWhiteSpace(ServerVersion) ? null : ServerVersion.Trim();
        target.InitialCatalog = string.IsNullOrWhiteSpace(Database) ? "master" : Database.Trim();
        target.DisplayName = string.IsNullOrWhiteSpace(Alias) ? target.DisplayName : Alias.Trim();
        target.AuthMode = (AuthMode)AuthIndex;
        target.UserName = IsUserEnabled && !string.IsNullOrWhiteSpace(UserName) ? UserName.Trim() : null;
        target.Encrypt = Encrypt;
        target.TrustServerCertificate = TrustServerCertificate;
        target.ConnectTimeoutSeconds = int.TryParse(ConnectTimeout.Trim(), out var t) ? Math.Clamp(t, MinTimeout, MaxTimeout) : 15;
        target.AdditionalParameters = string.IsNullOrWhiteSpace(AdditionalParameters) ? null : AdditionalParameters.Trim();
        target.ShowSystemDatabases = ShowSystemDatabases;
        target.DefaultTableTab = (DefaultObjectTab)DefaultTableTabIndex;
        target.SqlEditorAutocommit = SqlEditorAutocommit;
        target.SqlEditorFetchRows = int.TryParse(SqlEditorFetchRows.Trim(), out var rows) ? UserSettings.ClampFetchRows(rows) : null;
        target.AfterConnectScript = string.IsNullOrWhiteSpace(AfterConnectScript) ? null : AfterConnectScript;
        target.BeforeDisconnectScript = string.IsNullOrWhiteSpace(BeforeDisconnectScript) ? null : BeforeDisconnectScript;
        target.ColorMark = ColorMarks.Normalize(ColorMark);
        target.SettingsVersion = ConnectionProfileMigration.CurrentVersion;
        return target;
    }

    /// <summary>Recent servers: registered ones first, then the usual local names.</summary>
    public static IReadOnlyList<string> BuildRecentServers(IEnumerable<ConnectionProfile> registered)
    {
        var list = new List<string>();
        foreach (var p in registered)
        {
            var s = ServerNameText.Format(p);
            if (!list.Contains(s, StringComparer.OrdinalIgnoreCase)) list.Add(s);
        }
        foreach (var s in new[] { "localhost", @"localhost\SQLEXPRESS", @".\SQLEXPRESS", "(local)" })
            if (!list.Contains(s, StringComparer.OrdinalIgnoreCase)) list.Add(s);
        return list;
    }

    private void Set<T>(ref T field, T value, string? also1 = null, string? also2 = null, string? also3 = null,
        [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name);
        if (also1 is not null) Raise(also1);
        if (also2 is not null) Raise(also2);
        if (also3 is not null) Raise(also3);
    }

    private void Raise(string name)
    {
        if (name.Length > 0) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
