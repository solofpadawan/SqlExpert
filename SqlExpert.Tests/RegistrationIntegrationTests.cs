using Microsoft.Data.SqlClient;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;
using Xunit.Abstractions;

namespace SqlExpert.Tests;

/// <summary>
/// Live checks of the registration connection path (protocol prefixes, server version, database list, scripts)
/// against a real SQL Server. Read-only (SELECT / PRINT). Enabled by SQLEXPERT_IT_CONNSTR (Windows authentication);
/// without it every test returns immediately.
/// </summary>
public class RegistrationIntegrationTests
{
    private readonly ITestOutputHelper _out;
    private static readonly string? ConnStr = Environment.GetEnvironmentVariable("SQLEXPERT_IT_CONNSTR");

    public RegistrationIntegrationTests(ITestOutputHelper output) => _out = output;

    private ConnectionProfile? Profile(NetworkProtocol protocol = NetworkProtocol.Default)
    {
        if (string.IsNullOrWhiteSpace(ConnStr))
        {
            _out.WriteLine("SQLEXPERT_IT_CONNSTR não definido: teste de integração ignorado.");
            return null;
        }
        var b = new SqlConnectionStringBuilder(ConnStr);
        var parts = ServerNameText.Parse(b.DataSource);
        var p = new ConnectionProfile
        {
            DisplayName = "IT",
            Server = parts.Server,
            Instance = parts.Instance,
            Port = parts.Port,
            InitialCatalog = string.IsNullOrWhiteSpace(b.InitialCatalog) ? "master" : b.InitialCatalog,
            AuthMode = AuthMode.Windows,
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeoutSeconds = 10,
            Protocol = protocol
        };
        return ConnectionProfileMigration.Normalize(p);
    }

    private static SqlConnectionFactory Factory() => new(_ => null, new InMemoryCredentialStore());

    [Fact]
    public async Task Default_ReadsVersionAndDatabases()
    {
        var p = Profile();
        if (p is null) return;
        await using var conn = await Factory().OpenAsync(p);
        var version = await RegistrationScripts.GetServerVersionAsync(conn);
        _out.WriteLine(version);
        Assert.StartsWith("SQL Server", version);
        var dbs = await RegistrationScripts.ListDatabasesAsync(conn);
        Assert.Contains("master", dbs, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(p.InitialCatalog, dbs, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SharedMemory_ConnectsLocally()
    {
        var p = Profile(NetworkProtocol.SharedMemory);
        if (p is null) return;
        if (!ServerNameText.IsLocalHost(p.Server)) { _out.WriteLine("Servidor remoto: Shared Memory não se aplica."); return; }
        await using var conn = await Factory().OpenAsync(p);
        await using var cmd = new SqlCommand("SELECT net_transport FROM sys.dm_exec_connections WHERE session_id = @@SPID", conn);
        var transport = (string?)await cmd.ExecuteScalarAsync();
        _out.WriteLine($"{SqlConnectionFactory.BuildDataSource(p)} → {transport}");
        Assert.Equal("Shared memory", transport);
    }

    [Theory]
    [InlineData(NetworkProtocol.Tcp, "TCP")]
    [InlineData(NetworkProtocol.NamedPipes, "Named pipe")]
    public async Task OtherProtocols_UseTheirTransport_WhenEnabledOnServer(NetworkProtocol protocol, string expected)
    {
        var p = Profile(protocol);
        if (p is null) return;
        try
        {
            await using var conn = await Factory().OpenAsync(p);
            await using var cmd = new SqlCommand("SELECT net_transport FROM sys.dm_exec_connections WHERE session_id = @@SPID", conn);
            var transport = (string?)await cmd.ExecuteScalarAsync();
            _out.WriteLine($"{SqlConnectionFactory.BuildDataSource(p)} → {transport}");
            Assert.Equal(expected, transport);
        }
        catch (SqlException ex)
        {
            // The protocol may be disabled in SQL Server Configuration Manager (Express ships with TCP/NP off).
            _out.WriteLine($"{protocol} indisponível neste servidor: {ex.Message}");
        }
    }

    [Fact]
    public async Task Scripts_RunBatches_AndReportErrorsWithoutThrowing()
    {
        var p = Profile();
        if (p is null) return;
        await using var conn = await Factory().OpenAsync(p);
        var ok = await RegistrationScripts.RunAsync(conn, "SET NOCOUNT ON;\nSELECT 1;\nGO\nPRINT 'after connect'\nGO 2");
        Assert.Empty(ok);

        var bad = await RegistrationScripts.RunAsync(conn, "SELECT 1;\nGO\n\nSELECT * FROM tabela_que_nao_existe_xyz;\nGO\nSELECT 3;");
        Assert.Single(bad);
        Assert.StartsWith("Linha 4", bad[0]);
        Assert.Equal(System.Data.ConnectionState.Open, conn.State);
    }

    [Fact]
    public async Task AdditionalParameters_ReachTheServer()
    {
        var p = Profile();
        if (p is null) return;
        p.AdditionalParameters = "Application Name=SqlExpert-IT\nWorkstation ID=IT-WS";
        await using var conn = await Factory().OpenAsync(p);
        await using var cmd = new SqlCommand("SELECT APP_NAME() + '|' + HOST_NAME()", conn);
        Assert.Equal("SqlExpert-IT|IT-WS", (string?)await cmd.ExecuteScalarAsync());
    }
}
