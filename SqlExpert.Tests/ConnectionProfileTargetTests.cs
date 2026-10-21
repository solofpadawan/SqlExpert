using SqlExpert.Core.Models;

namespace SqlExpert.Tests;

public class ConnectionProfileTargetTests
{
    private static ConnectionProfile Make() => new()
    {
        DisplayName = "SQL Server",
        Server = "localhost",
        Instance = "SQLEXPRESS",
        Port = 1433,
        InitialCatalog = "SqlExpertTeste",
        AuthMode = AuthMode.Windows
    };

    [Fact]
    public void SameTarget_IgnoresIdDisplayNameAndCase()
    {
        var a = Make();
        var b = Make();
        b.DisplayName = "Outro nome";
        b.Server = "LOCALHOST";
        b.InitialCatalog = "sqlexpertteste";
        Assert.NotEqual(a.Id, b.Id);
        Assert.True(a.HasSameTarget(b));
    }

    [Fact]
    public void DifferentDatabase_IsNotSameTarget()
    {
        var a = Make();
        var b = Make();
        b.InitialCatalog = "master";
        Assert.False(a.HasSameTarget(b));
    }

    [Fact]
    public void SqlLogin_DifferentUser_IsNotSameTarget()
    {
        var a = Make(); a.AuthMode = AuthMode.SqlLogin; a.UserName = "sa";
        var b = Make(); b.AuthMode = AuthMode.SqlLogin; b.UserName = "app";
        Assert.False(a.HasSameTarget(b));
        b.UserName = "SA";
        Assert.True(a.HasSameTarget(b));
    }

    [Fact]
    public void DifferentAuthOrInstance_IsNotSameTarget()
    {
        var a = Make();
        var b = Make(); b.AuthMode = AuthMode.SqlLogin; b.UserName = "sa";
        Assert.False(a.HasSameTarget(b));
        var c = Make(); c.Instance = null;
        Assert.False(a.HasSameTarget(c));
    }
}
