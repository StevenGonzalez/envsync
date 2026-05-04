namespace EnvSync.Core.Providers.Vault;

/// <summary>
/// Identifies a secret in a HashiCorp Vault KV v2 engine.
/// </summary>
public sealed record VaultKvReference
{
    /// <summary>
    /// Creates a Vault KV v2 secret reference.
    /// </summary>
    /// <param name="address">The absolute Vault server address.</param>
    /// <param name="mount">The KV v2 engine mount path.</param>
    /// <param name="secretPath">The path within the mount.</param>
    public VaultKvReference(string address, string mount, string secretPath)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Scheme))
        {
            throw new ArgumentException("Vault address must be an absolute URI.", nameof(address));
        }

        if (string.IsNullOrWhiteSpace(mount) || mount.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("Vault mount must be a single non-empty path segment.", nameof(mount));
        }

        if (string.IsNullOrWhiteSpace(secretPath) || secretPath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Vault secret path must be a non-empty relative path.", nameof(secretPath));
        }

        Address = address;
        Mount = mount;
        SecretPath = secretPath;
    }

    /// <summary>
    /// Full Vault server address including scheme and port, e.g. <c>https://vault.example.com:8200</c>.
    /// Defaults to <c>http://127.0.0.1:8200</c> if not supplied.
    /// </summary>
    public string Address { get; init; }

    /// <summary>
    /// Gets the KV v2 engine mount path, e.g. <c>secret</c>.
    /// </summary>
    public string Mount { get; init; }

    /// <summary>
    /// The path within the mount to the secret that holds all env var key-value pairs,
    /// e.g. <c>myapp/production</c>.
    /// </summary>
    public string SecretPath { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{Mount}/{SecretPath}";
}
