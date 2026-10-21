using System.Text.Json.Nodes;
using SqlExpert.Core.Models;
using SqlExpert.Core.Registration;
using SqlExpert.Core.Services;

namespace SqlExpert.Tests;

/// <summary>Registration model (Database Properties): persistence, migration of old files, form validation.</summary>
public class DatabaseRegistrationTests
{
    private static string TempPath(string name = "connections.json") =>
        Path.Combine(Path.GetTempPath(), "SqlExpert-tests", Guid.NewGuid().ToString("N"), name);

    private static ConnectionProfile FullProfile() => new()
    {
        DisplayName = "Produção",
        Server = "srv01",
        Instance = "SQL2022",
        Port = 1433,
        InitialCatalog = "Vendas",
        AuthMode = AuthMode.SqlLogin,
        UserName = "app",
        PasswordSecretId = "pwd-x",
        Encrypt = true,
        TrustServerCertificate = true,
        ConnectTimeoutSeconds = 30,
        Location = ServerLocation.Remote,
        Protocol = NetworkProtocol.Tcp,
        ServerVersion = "SQL Server 2022 (16.0.1000.6) Express",
        AdditionalParameters = "Application Intent=ReadOnly\nWorkstation ID=PDV01",
        ShowSystemDatabases = false,
        DefaultTableTab = DefaultObjectTab.Data,
        SqlEditorAutocommit = false,
        SqlEditorFetchRows = 250,
        AfterConnectScript = "SET NOCOUNT ON;",
        BeforeDisconnectScript = "INSERT INTO log VALUES (1);",
        ColorMark = "#D32F2F"
    };

    [Fact]
    public async Task Store_RoundTrips_AllRegistrationFields()
    {
        var path = TempPath();
        var store = new ConnectionProfileStore(path);
        var p = FullProfile();
        await store.SaveAsync(new[] { p });
        var loaded = (await store.LoadAsync()).Single();

        Assert.Equal(p.Id, loaded.Id);
        Assert.Equal("Produção", loaded.DisplayName);
        Assert.Equal(ServerLocation.Remote, loaded.Location);
        Assert.Equal(NetworkProtocol.Tcp, loaded.Protocol);
        Assert.Equal(p.ServerVersion, loaded.ServerVersion);
        Assert.Equal(p.AdditionalParameters, loaded.AdditionalParameters);
        Assert.False(loaded.ShowSystemDatabases);
        Assert.Equal(DefaultObjectTab.Data, loaded.DefaultTableTab);
        Assert.False(loaded.SqlEditorAutocommit);
        Assert.Equal(250, loaded.SqlEditorFetchRows);
        Assert.Equal(p.AfterConnectScript, loaded.AfterConnectScript);
        Assert.Equal(p.BeforeDisconnectScript, loaded.BeforeDisconnectScript);
        Assert.Equal("#D32F2F", loaded.ColorMark);
        Assert.Equal(ConnectionProfileMigration.CurrentVersion, loaded.SettingsVersion);
        Assert.Equal("pwd-x", loaded.PasswordSecretId);

        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"protocol\": \"tcp\"", json);
        Assert.Contains("\"location\": \"remote\"", json);
        Assert.DoesNotContain("\"password\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Store_LoadsLegacyFile_WithDefaults()
    {
        // connections.json written by the versions before the Database Properties dialog.
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
            [
              {
                "id": "a1",
                "displayName": "SQL Server",
                "server": "localhost",
                "instance": "SQLEXPRESS",
                "port": 1433,
                "initialCatalog": "SqlExpertTeste",
                "authMode": "windows",
                "userName": null,
                "passwordSecretId": null,
                "trustServerCertificate": true,
                "encrypt": true,
                "connectTimeoutSeconds": 15
              },
              {
                "id": "b2",
                "displayName": "Remoto",
                "server": "tcp:192.168.0.10,1500",
                "port": 1433,
                "initialCatalog": "",
                "authMode": "sqlLogin",
                "userName": "sa",
                "passwordSecretId": "pwd-b2",
                "encrypt": false,
                "connectTimeoutSeconds": 0
              }
            ]
            """);

        var loaded = await new ConnectionProfileStore(path).LoadAsync();
        Assert.Equal(2, loaded.Count);

        var local = loaded[0];
        Assert.Equal("SQL Server", local.DisplayName);
        Assert.Equal(ServerLocation.Local, local.Location);
        Assert.Equal(NetworkProtocol.Default, local.Protocol);
        Assert.Equal("SQLEXPRESS", local.Instance);
        Assert.True(local.SqlEditorAutocommit);
        Assert.Null(local.SqlEditorFetchRows);
        Assert.True(local.ShowSystemDatabases);
        Assert.Equal(DefaultObjectTab.Fields, local.DefaultTableTab);
        Assert.Null(local.ColorMark);
        Assert.Equal(ConnectionProfileMigration.CurrentVersion, local.SettingsVersion);

        var remote = loaded[1];
        Assert.Equal("192.168.0.10", remote.Server);
        Assert.Equal(1500, remote.Port);
        Assert.Equal(NetworkProtocol.Tcp, remote.Protocol);
        Assert.Equal(ServerLocation.Remote, remote.Location);
        Assert.Equal("master", remote.InitialCatalog);
        Assert.Equal(15, remote.ConnectTimeoutSeconds);
        Assert.Equal("pwd-b2", remote.PasswordSecretId);
    }

    [Fact]
    public void Migration_IsIdempotent_AndSplitsInstance()
    {
        var p = new ConnectionProfile { Server = @"np:srv\INST", ColorMark = "abc", AdditionalParameters = "  " };
        ConnectionProfileMigration.Normalize(p);
        Assert.Equal("srv", p.Server);
        Assert.Equal("INST", p.Instance);
        Assert.Equal(NetworkProtocol.NamedPipes, p.Protocol);
        Assert.Equal("#AABBCC", p.ColorMark);
        Assert.Null(p.AdditionalParameters);
        var json1 = System.Text.Json.JsonSerializer.Serialize(p);
        ConnectionProfileMigration.Normalize(p);
        Assert.Equal(json1, System.Text.Json.JsonSerializer.Serialize(p));
    }

    [Fact]
    public void Clone_CopiesEveryField_AndIsIndependent()
    {
        var p = FullProfile();
        var c = p.Clone();
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(p), System.Text.Json.JsonSerializer.Serialize(c));
        c.DisplayName = "outro";
        Assert.Equal("Produção", p.DisplayName);
    }

    [Fact]
    public void ConnectionSettings_IgnoreAliasColorScriptsAndEditorDefaults()
    {
        var a = FullProfile();
        var b = a.Clone();
        b.DisplayName = "x";
        b.ColorMark = "#000000";
        b.AfterConnectScript = "SELECT 1";
        b.SqlEditorFetchRows = 5;
        b.ServerVersion = null;
        Assert.True(a.HasSameConnectionSettings(b));

        b.Protocol = NetworkProtocol.NamedPipes;
        Assert.False(a.HasSameConnectionSettings(b));
        b = a.Clone(); b.AdditionalParameters = "Application Intent=ReadWrite";
        Assert.False(a.HasSameConnectionSettings(b));
        b = a.Clone(); b.TrustServerCertificate = false;
        Assert.False(a.HasSameConnectionSettings(b));
    }

    // ===== Form =====

    [Fact]
    public void Form_FromProfile_ApplyTo_RoundTrips()
    {
        var p = FullProfile();
        var form = RegistrationForm.FromProfile(p);
        Assert.Equal(@"srv01\SQL2022", form.ServerName);
        Assert.Equal(1, form.LocationIndex);
        Assert.Equal((int)NetworkProtocol.Tcp, form.ProtocolIndex);
        Assert.Empty(form.Validate());

        var result = form.ApplyTo(p.Clone());
        var expected = p.Clone();
        expected.SettingsVersion = ConnectionProfileMigration.CurrentVersion;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(expected), System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Form_Rename_ChangesOnlyAlias()
    {
        var p = new ConnectionProfile { DisplayName = "SQL Server", Server = "localhost", Instance = "SQLEXPRESS", InitialCatalog = "SqlExpertTeste" };
        ConnectionProfileMigration.Normalize(p);
        var form = RegistrationForm.FromProfile(p);
        form.Alias = "  Teste local  ";
        var result = form.ApplyTo(p.Clone());
        Assert.Equal("Teste local", result.DisplayName);
        Assert.Equal(p.Id, result.Id);
        Assert.True(p.HasSameConnectionSettings(result));
    }

    [Fact]
    public void Form_ServerNameWithPortAndPrefix()
    {
        var form = RegistrationForm.FromProfile(new ConnectionProfile());
        form.Alias = "x";
        form.LocationIndex = 1;
        form.ServerName = "np:db01,1500";
        var r = form.ApplyTo(new ConnectionProfile());
        Assert.Equal("db01", r.Server);
        Assert.Null(r.Instance);
        Assert.Equal(1500, r.Port);
        Assert.Equal(NetworkProtocol.NamedPipes, r.Protocol);
        Assert.Equal(ServerLocation.Remote, r.Location);
    }

    [Fact]
    public void Form_Validate_ReportsProblems()
    {
        var form = RegistrationForm.FromProfile(new ConnectionProfile());
        form.Alias = " ";
        form.AuthIndex = 1;
        form.UserName = "";
        form.ConnectTimeout = "abc";
        form.SqlEditorFetchRows = "0";
        form.ColorMark = "verde";
        form.AdditionalParameters = "Password=123";
        var errors = form.Validate();
        Assert.Contains(errors, e => e.Contains("Alias"));
        Assert.Contains(errors, e => e.Contains("User Name"));
        Assert.Contains(errors, e => e.Contains("timeout"));
        Assert.Contains(errors, e => e.Contains("SQL Editor"));
        Assert.Contains(errors, e => e.Contains("Color"));
        Assert.Contains(errors, e => e.Contains("Password"));

        // Test Connect only cares about connection fields.
        var conn = form.Validate(connectionOnly: true);
        Assert.DoesNotContain(conn, e => e.Contains("Alias") || e.Contains("Color") || e.Contains("SQL Editor"));
    }

    [Fact]
    public void Form_SharedMemory_OnlyLocal()
    {
        var form = RegistrationForm.FromProfile(new ConnectionProfile { DisplayName = "x" });
        form.ProtocolIndex = (int)NetworkProtocol.SharedMemory;
        form.ServerName = "localhost";
        form.LocationIndex = 0;
        Assert.Empty(form.Validate());
        form.LocationIndex = 1;
        Assert.Contains(form.Validate(), e => e.Contains("Shared Memory"));
        form.LocationIndex = 0;
        form.ServerName = "srv-remoto";
        Assert.Contains(form.Validate(), e => e.Contains("Shared Memory"));
    }

    [Fact]
    public void Form_WindowsAuth_DisablesUser_AndClearsIt()
    {
        var form = RegistrationForm.FromProfile(new ConnectionProfile { DisplayName = "x", AuthMode = AuthMode.SqlLogin, UserName = "sa" });
        Assert.True(form.IsSqlLogin);
        Assert.True(form.IsUserEnabled);
        var changed = new List<string?>();
        form.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        form.AuthIndex = 0;
        Assert.Contains(nameof(RegistrationForm.IsSqlLogin), changed);
        Assert.Contains(nameof(RegistrationForm.IsUserEnabled), changed);
        Assert.False(form.IsUserEnabled);
        Assert.Null(form.ApplyTo(new ConnectionProfile()).UserName);
    }

    [Fact]
    public void Form_CopyFrom_KeepsAliasAndColor()
    {
        var form = RegistrationForm.FromProfile(new ConnectionProfile { DisplayName = "Meu banco", ColorMark = "#1E88E5" });
        form.CopyFrom(FullProfile());
        Assert.Equal("Meu banco", form.Alias);
        Assert.Equal("#1E88E5", form.ColorMark);
        Assert.Equal(@"srv01\SQL2022", form.ServerName);
        Assert.Equal("Vendas", form.Database);
        Assert.Equal("app", form.UserName);
        Assert.Equal("250", form.SqlEditorFetchRows);
        Assert.Equal("SET NOCOUNT ON;", form.AfterConnectScript);
    }

    [Fact]
    public void Form_BlankFetchRows_MeansGlobal()
    {
        var form = RegistrationForm.FromProfile(FullProfile());
        form.SqlEditorFetchRows = "";
        Assert.Null(form.ApplyTo(new ConnectionProfile()).SqlEditorFetchRows);
    }

    [Fact]
    public void RecentServers_RegisteredFirst_NoDuplicates()
    {
        var list = RegistrationForm.BuildRecentServers(new[]
        {
            new ConnectionProfile { Server = "localhost", Instance = "SQLEXPRESS" },
            new ConnectionProfile { Server = "srv", Port = 1500 },
            new ConnectionProfile { Server = "LOCALHOST", Instance = "sqlexpress" }
        });
        Assert.Equal(@"localhost\SQLEXPRESS", list[0]);
        Assert.Equal("srv,1500", list[1]);
        Assert.Equal(1, list.Count(s => s.Equals(@"localhost\SQLEXPRESS", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("(local)", list);
    }

    // ===== Helpers =====

    [Theory]
    [InlineData("localhost", "localhost", null, 1433, null)]
    [InlineData(@"srv\INST", "srv", "INST", 1433, null)]
    [InlineData("srv,1500", "srv", null, 1500, null)]
    [InlineData(@"tcp:srv\INST,1501", "srv", "INST", 1501, NetworkProtocol.Tcp)]
    [InlineData(@"lpc:.\SQLEXPRESS", ".", "SQLEXPRESS", 1433, NetworkProtocol.SharedMemory)]
    [InlineData(@"\SQLEXPRESS", "localhost", "SQLEXPRESS", 1433, null)]
    public void ServerName_Parse(string text, string server, string? instance, int port, NetworkProtocol? protocol)
    {
        var p = ServerNameText.Parse(text);
        Assert.Equal(server, p.Server);
        Assert.Equal(instance, p.Instance);
        Assert.Equal(port, p.Port);
        Assert.Equal(protocol, p.Protocol);
    }

    [Theory]
    [InlineData("srv,abc")]
    [InlineData("srv,70000")]
    [InlineData("srv,0")]
    public void ServerName_InvalidPort_Throws(string text) => Assert.Throws<FormatException>(() => ServerNameText.Parse(text));

    [Fact]
    public void ServerName_Format()
    {
        Assert.Equal("localhost", ServerNameText.Format("localhost", null, 1433));
        Assert.Equal(@"srv\I", ServerNameText.Format("srv", "I", 1433));
        Assert.Equal("srv,1500", ServerNameText.Format("srv", null, 1500));
        Assert.Equal(@"srv\I,1500", ServerNameText.Format("srv", "I", 1500));
        Assert.Equal("localhost", ServerNameText.Format(" ", null, 0));
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData(".", true)]
    [InlineData("(local)", true)]
    [InlineData("tcp:127.0.0.1", true)]
    [InlineData("192.168.0.10", false)]
    [InlineData("srv01", false)]
    public void ServerName_IsLocalHost(string server, bool expected) => Assert.Equal(expected, ServerNameText.IsLocalHost(server));

    [Theory]
    [InlineData("16.0.1000.6", "Express Edition (64-bit)", null, "SQL Server 2022 (16.0.1000.6) Express")]
    [InlineData("15.0.2000.5", "Developer Edition (64-bit)", 3, "SQL Server 2019 (15.0.2000.5) Developer")]
    [InlineData("10.50.6000.34", "Standard Edition", 2, "SQL Server 2008 R2 (10.50.6000.34) Standard")]
    [InlineData("16.0.4135.4", "Enterprise Edition: Core-based Licensing (64-bit)", 3, "SQL Server 2022 (16.0.4135.4) Enterprise")]
    [InlineData("12.0.2000.8", "SQL Azure", 5, "Azure SQL Database (12.0.2000.8)")]
    [InlineData("99.0.1", "", null, "SQL Server (99.0.1)")]
    public void ServerVersion_Format(string version, string edition, int? engine, string expected)
        => Assert.Equal(expected, ServerVersionText.Format(version, edition, engine));

    [Theory]
    [InlineData("#d32f2f", "#D32F2F")]
    [InlineData("d32f2f", "#D32F2F")]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("", null)]
    [InlineData("red", null)]
    [InlineData("#12345", null)]
    public void ColorMark_Normalize(string text, string? expected) => Assert.Equal(expected, ColorMarks.Normalize(text));

    [Fact]
    public void ColorMark_PaletteIsValid()
    {
        Assert.Equal(16, ColorMarks.Palette.Count);
        Assert.All(ColorMarks.Palette, c => Assert.Equal(c.Hex, ColorMarks.Normalize(c.Hex)));
    }

    [Fact]
    public void ExplorerOrder_ByAliasOrAsRegistered()
    {
        var a = new ConnectionProfile { DisplayName = "zeta" };
        var b = new ConnectionProfile { DisplayName = "Alfa" };
        var c = new ConnectionProfile { DisplayName = "beta" };
        var session = new ConnectionSession();
        session.ReplaceAll(new[] { a, b, c });
        Assert.Equal(new[] { a, b, c }, session.Ordered(byAlias: false));
        Assert.Equal(new[] { b, c, a }, session.Ordered(byAlias: true));
    }

    [Theory]
    [InlineData("master", true)]
    [InlineData("TEMPDB", true)]
    [InlineData("SqlExpertTeste", false)]
    public void SystemDatabases(string name, bool expected) => Assert.Equal(expected, MetadataSql.IsSystemDatabase(name));

    [Fact]
    public void ObjectDescriptions_ReadsMsDescription()
    {
        Assert.Contains("MS_Description", MetadataSql.ObjectDescriptions);
        Assert.True(MetadataSql.HasObjectDescriptions(ObjectKind.Table));
        Assert.False(MetadataSql.HasObjectDescriptions(ObjectKind.Role));
    }

    // ===== Settings (explorer toggles) =====

    [Fact]
    public void UserSettings_ExplorerToggles_RoundTrip_AndDefaultOff()
    {
        var path = TempPath("settings.json");
        var store = new UserSettingsStore(path);
        var d = store.Load();
        Assert.False(d.ExplorerSortByAlias);
        Assert.False(d.HideDisconnectedDatabases);
        Assert.False(d.ShowObjectsDescription);

        store.Save(new UserSettings { ExplorerSortByAlias = true, HideDisconnectedDatabases = true, ShowObjectsDescription = true, SqlFetchRows = 77 });
        var l = store.Load();
        Assert.True(l.ExplorerSortByAlias);
        Assert.True(l.HideDisconnectedDatabases);
        Assert.True(l.ShowObjectsDescription);
        Assert.Equal(77, l.SqlFetchRows);
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(json.ContainsKey("hideDisconnectedDatabases"));
    }
}
