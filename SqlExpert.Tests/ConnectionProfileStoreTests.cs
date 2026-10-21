using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.Tests;

public class ConnectionProfileStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTrips_WithoutPassword()
    {
        var path = Path.Combine(Path.GetTempPath(), "SqlExpert-tests", Guid.NewGuid().ToString("N"), "connections.json");
        var store = new ConnectionProfileStore(path);
        var profile = new ConnectionProfile
        {
            DisplayName = "Local",
            Server = "localhost",
            InitialCatalog = "master",
            AuthMode = AuthMode.SqlLogin,
            UserName = "sa",
            PasswordSecretId = "pwd-abc",
            TrustServerCertificate = true
        };

        await store.SaveAsync(new[] { profile });
        var loaded = await store.LoadAsync();

        Assert.Single(loaded);
        Assert.Equal("Local", loaded[0].DisplayName);
        Assert.Equal("pwd-abc", loaded[0].PasswordSecretId);
        Assert.Equal(AuthMode.SqlLogin, loaded[0].AuthMode);

        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("Password\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("s3cret", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("passwordSecretId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InMemoryCredentialStore_SetGetDelete()
    {
        var store = new InMemoryCredentialStore();
        await store.SetSecretAsync("a", "secret");
        Assert.Equal("secret", await store.GetSecretAsync("a"));
        await store.DeleteSecretAsync("a");
        Assert.Null(await store.GetSecretAsync("a"));
    }
}
