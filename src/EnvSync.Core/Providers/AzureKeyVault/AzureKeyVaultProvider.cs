using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using EnvSync.Core.Model;

namespace EnvSync.Core.Providers.AzureKeyVault;

/// <summary>
/// Reads and writes environment variables stored as Azure Key Vault secrets.
/// <para>
/// Azure Key Vault secret names only allow alphanumeric characters and hyphens. Environment keys
/// use <c>SCREAMING_SNAKE_CASE</c>, so underscores are translated to hyphens on write and back
/// to underscores on read, for example <c>DATABASE_URL</c> maps to <c>DATABASE-URL</c>.
/// </para>
/// <para>
/// Authentication is handled by <see cref="DefaultAzureCredential"/>, which resolves credentials
/// from (in order): environment variables, workload identity, managed identity, Visual Studio,
/// Azure CLI, and Azure PowerShell. This covers local development and production deployments
/// without any code changes.
/// </para>
/// </summary>
public sealed class AzureKeyVaultProvider : IEnvironmentProvider
{
    private readonly SecretClient _client;

    /// <summary>
    /// Creates a provider that authenticates via <see cref="DefaultAzureCredential"/>.
    /// </summary>
    public AzureKeyVaultProvider(AzureKeyVaultReference reference, TokenCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(reference);

        Reference = reference;
        _client = new SecretClient(new Uri(reference.VaultUri), credential ?? new DefaultAzureCredential());
    }

    /// <summary>
    /// Creates a provider with a pre-configured <see cref="SecretClient"/>, useful for testing
    /// or advanced scenarios such as sovereign-cloud URIs.
    /// </summary>
    public AzureKeyVaultProvider(AzureKeyVaultReference reference, SecretClient client)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(client);

        Reference = reference;
        _client = client;
    }

    /// <summary>
    /// Gets the Azure Key Vault reference.
    /// </summary>
    public AzureKeyVaultReference Reference { get; }

    /// <inheritdoc />
    public string Description => $"azurekeyvault:{Reference.VaultName}";

    /// <inheritdoc />
    public async Task<EnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var values = new List<EnvironmentValue>();

        await foreach (var properties in _client.GetPropertiesOfSecretsAsync(cancellationToken).ConfigureAwait(false))
        {
            // Skip disabled secrets; they are not logically present in the environment.
            if (properties.Enabled != true)
            {
                continue;
            }

            try
            {
                var response = await _client.GetSecretAsync(properties.Name, cancellationToken: cancellationToken).ConfigureAwait(false);
                var key = VaultNameToEnvKey(response.Value.Name);
                values.Add(EnvironmentValue.Available(key, response.Value.Value));
            }
            catch (RequestFailedException exception) when (exception.Status == 403)
            {
                // Access denied on an individual secret; surface as hidden rather than failing
                // the entire read, since partial visibility is better than a hard error.
                var key = VaultNameToEnvKey(properties.Name);
                values.Add(EnvironmentValue.Hidden(key));
            }
        }

        return new EnvironmentSnapshot(Description, values);
    }

    /// <inheritdoc />
    public async Task<ProviderWriteResult> WriteAsync(
        IReadOnlyCollection<ResolvedEnvironmentValue> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var value in values)
        {
            EnvironmentKeyValidator.ThrowIfInvalid(value.Key);

            var secretName = EnvKeyToVaultName(value.Key);
            await _client.SetSecretAsync(secretName, value.Value, cancellationToken).ConfigureAwait(false);
        }

        return new ProviderWriteResult(values.Count);
    }

    // Azure Key Vault names only allow alphanumeric characters and hyphens (no underscores).
    private static string VaultNameToEnvKey(string vaultName) => vaultName.Replace('-', '_');

    private static string EnvKeyToVaultName(string envKey) => envKey.Replace('_', '-');
}
