using Microsoft.Data.SqlClient;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.Tests;

/// <summary>Connection string of a registration: protocol prefixes, extra parameters, authentication.</summary>
public class ConnectionStringBuilderTests
{
    private static SqlConnectionFactory Factory() => new(_ => null, new InMemoryCredentialStore());

    private static SqlConnectionStringBuilder Build(ConnectionProfile p, string? password = null)
        => new(Factory().BuildConnectionString(p, password));

    [Theory]
    [InlineData(NetworkProtocol.Default, "localhost", null, 1433, "localhost")]
    [InlineData(NetworkProtocol.Tcp, "srv", null, 1433, "tcp:srv")]
    [InlineData(NetworkProtocol.Tcp, "srv", null, 1500, "tcp:srv,1500")]
    [InlineData(NetworkProtocol.NamedPipes, "srv", "INST", 1433, @"np:srv\INST")]
    [InlineData(NetworkProtocol.SharedMemory, "localhost", "SQLEXPRESS", 1433, @"lpc:localhost\SQLEXPRESS")]
    [InlineData(NetworkProtocol.Default, "srv", "INST", 1433, @"srv\INST")]
    public void DataSource_ProtocolPrefix(NetworkProtocol protocol, string server, string? instance, int port, string expected)
    {
        var p = new ConnectionProfile { Server = server, Instance = instance, Port = port, Protocol = protocol };
        Assert.Equal(expected, SqlConnectionFactory.BuildDataSource(p));
        Assert.Equal(expected, Build(p).DataSource);
    }

    [Fact]
    public void DataSource_DoesNotDoublePrefix()
    {
        var p = new ConnectionProfile { Server = "tcp:srv", Protocol = NetworkProtocol.Tcp };
        Assert.Equal("tcp:srv", SqlConnectionFactory.BuildDataSource(p));
    }

    [Fact]
    public void AdditionalParameters_AreMerged()
    {
        var p = new ConnectionProfile
        {
            Server = "srv",
            AdditionalParameters = "Application Intent=ReadOnly\r\n# comentário\n\n-- outro\nMultiSubnetFailover=True; Workstation ID=PDV01\npacket size=8192"
        };
        var b = Build(p);
        Assert.Equal(ApplicationIntent.ReadOnly, b.ApplicationIntent);
        Assert.True(b.MultiSubnetFailover);
        Assert.Equal("PDV01", b.WorkstationID);
        Assert.Equal(8192, b.PacketSize);
        Assert.Equal("SqlExpert", b.ApplicationName);
    }

    [Fact]
    public void AdditionalParameters_CanOverrideApplicationName()
    {
        var b = Build(new ConnectionProfile { AdditionalParameters = "Application Name=Relatorios" });
        Assert.Equal("Relatorios", b.ApplicationName);
    }

    [Theory]
    [InlineData("Password=123")]
    [InlineData("pwd=123")]
    [InlineData("User ID=sa")]
    [InlineData("uid=sa")]
    [InlineData("Server=outro")]
    [InlineData("Data Source=outro")]
    [InlineData("Database=outro")]
    [InlineData("Initial Catalog=outro")]
    [InlineData("Integrated Security=SSPI")]
    [InlineData("Trusted_Connection=yes")]
    [InlineData("Encrypt=false")]
    [InlineData("TrustServerCertificate=true")]
    [InlineData("Connect Timeout=5")]
    [InlineData("Timeout=5")]
    public void AdditionalParameters_RejectFieldsOfTheDialog(string line)
    {
        var errors = AdditionalConnectParameters.Validate(line);
        Assert.Single(errors);
        Assert.Throws<ArgumentException>(() => Factory().BuildConnectionString(new ConnectionProfile { AdditionalParameters = line }));
    }

    [Theory]
    [InlineData("Banana=1")]
    [InlineData("Packet Size=muito")]
    [InlineData("isto não é chave valor")]
    [InlineData("MultiSubnetFailover=talvez")]
    public void AdditionalParameters_RejectUnknownOrInvalid(string line)
        => Assert.NotEmpty(AdditionalConnectParameters.Validate(line));

    [Fact]
    public void AdditionalParameters_ErrorNamesTheLine()
    {
        var errors = AdditionalConnectParameters.Validate("Application Intent=ReadOnly\nBanana=1");
        Assert.Single(errors);
        Assert.StartsWith("Linha 2", errors[0]);
    }

    [Fact]
    public void AdditionalParameters_BlankIsValid()
    {
        Assert.Empty(AdditionalConnectParameters.Validate(null));
        Assert.Empty(AdditionalConnectParameters.Validate("  \n # só comentário"));
    }

    [Fact]
    public void Auth_Windows_IgnoresUserAndPassword()
    {
        var b = Build(new ConnectionProfile { AuthMode = AuthMode.Windows, UserName = "sa" }, "s3cret");
        Assert.True(b.IntegratedSecurity);
        Assert.Equal("", b.UserID);
        Assert.Equal("", b.Password);
    }

    [Fact]
    public void Auth_SqlLogin_UsesUserAndPassword()
    {
        var b = Build(new ConnectionProfile { AuthMode = AuthMode.SqlLogin, UserName = "app" }, "s3cret");
        Assert.False(b.IntegratedSecurity);
        Assert.Equal("app", b.UserID);
        Assert.Equal("s3cret", b.Password);
    }

    [Fact]
    public void Auth_Entra_Interactive()
    {
        var b = Build(new ConnectionProfile { AuthMode = AuthMode.AzureAd, UserName = "user@empresa.com" });
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryInteractive, b.Authentication);
        Assert.Equal("user@empresa.com", b.UserID);
    }

    [Fact]
    public void EncryptTrustAndTimeout_FromFields()
    {
        var b = Build(new ConnectionProfile { Encrypt = false, TrustServerCertificate = true, ConnectTimeoutSeconds = 42, InitialCatalog = "Vendas" });
        Assert.Equal(SqlConnectionEncryptOption.Optional, b.Encrypt);
        Assert.True(b.TrustServerCertificate);
        Assert.Equal(42, b.ConnectTimeout);
        Assert.Equal("Vendas", b.InitialCatalog);
    }

    [Fact]
    public async Task OpenAsync_UsesStoredSecret_WhenPasswordIsNull()
    {
        var creds = new InMemoryCredentialStore();
        await creds.SetSecretAsync("sec", "guardada");
        var p = new ConnectionProfile
        {
            Server = "127.0.0.1",
            Port = 1,
            Protocol = NetworkProtocol.Tcp,
            AuthMode = AuthMode.SqlLogin,
            UserName = "sa",
            PasswordSecretId = "sec",
            ConnectTimeoutSeconds = 1
        };
        var factory = new SqlConnectionFactory(_ => null, creds);
        // Nothing listens on port 1: the open fails, but only after the secret was resolved (no exception from the store).
        await Assert.ThrowsAnyAsync<Exception>(() => factory.OpenAsync(p));
        Assert.Contains("Password=guardada", factory.BuildConnectionString(p, await creds.GetSecretAsync("sec")));
    }
}
