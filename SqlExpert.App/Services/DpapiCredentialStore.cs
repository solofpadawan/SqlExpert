using System.IO;
using System.Security.Cryptography;
using System.Text;
using SqlExpert.Core.Abstractions;

namespace SqlExpert.App.Services;

/// <summary>
/// Production credential store for Windows WPF.
/// Encrypts secrets with Windows DPAPI (<see cref="ProtectedData"/>) and stores
/// ciphertext under %AppData%/SqlExpert/secrets/{secretId}.bin.
/// Passwords are NEVER written to connections.json.
/// </summary>
public sealed class DpapiCredentialStore : ICredentialStore
{
    private readonly string _directory;

    public DpapiCredentialStore(string? directory = null)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _directory = directory;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _directory = Path.Combine(appData, "SqlExpert", "secrets");
        }

        Directory.CreateDirectory(_directory);
    }

    public Task SetSecretAsync(string secretId, string secret, CancellationToken cancellationToken = default)
    {
        var path = PathFor(secretId);
        var plain = Encoding.UTF8.GetBytes(secret);
        var cipher = ProtectedData.Protect(plain, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, cipher);
        return Task.CompletedTask;
    }

    public Task<string?> GetSecretAsync(string secretId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(secretId);
        if (!File.Exists(path))
            return Task.FromResult<string?>(null);

        var cipher = File.ReadAllBytes(path);
        var plain = ProtectedData.Unprotect(cipher, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        return Task.FromResult<string?>(Encoding.UTF8.GetString(plain));
    }

    public Task DeleteSecretAsync(string secretId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(secretId);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string secretId)
    {
        // Keep filename safe
        var safe = string.Concat(secretId.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        return Path.Combine(_directory, safe + ".bin");
    }
}
