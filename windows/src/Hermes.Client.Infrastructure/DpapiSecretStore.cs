using System.Security.Cryptography;
using System.Text;
using Hermes.Client.Core;

namespace Hermes.Client.Infrastructure;

/// <summary>
/// Windows DPAPI secret storage (DataProtectionScope.CurrentUser).
/// Secrets are DPAPI-encrypted files under %LocalAppData%\Hermes.Client\secrets —
/// decryptable only by this Windows user account. The gateway Bearer key
/// lives here, never in plaintext config.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Hermes.Client", "secrets");

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(PathFor(key), bytes, cancellationToken);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        var safe = string.Concat(key.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_directory, safe + ".dat");
    }
}
