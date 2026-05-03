using Amazon;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using EnvSync.Core.Model;

namespace EnvSync.Core.Providers.AwsSsm;

/// <summary>
/// Reads and writes environment variables stored as AWS Systems Manager Parameter Store parameters
/// under a common path prefix.
/// <para>
/// Parameters of type <c>SecureString</c> are surfaced as <see cref="EnvironmentValue.Hidden"/>
/// because reading their plaintext requires <c>kms:Decrypt</c> permission that may not be
/// universally available. Writes honour the <see cref="Model.ResolvedEnvironmentValue.Secret"/>
/// flag to determine whether the parameter should be stored as <c>String</c> or <c>SecureString</c>.
/// </para>
/// </summary>
public sealed class AwsSsmProvider : IEnvironmentProvider, IDisposable
{
    private readonly IAmazonSimpleSystemsManagement _client;
    private readonly bool _ownsClient;

    // Normalised prefix always ends with '/' so that key extraction is a simple substring.
    private readonly string _normalizedPrefix;

    public AwsSsmProvider(AwsSsmReference reference, IAmazonSimpleSystemsManagement? client = null)
    {
        ArgumentNullException.ThrowIfNull(reference);

        Reference = reference;
        _normalizedPrefix = reference.PathPrefix.TrimEnd('/') + '/';

        if (client is not null)
        {
            _client = client;
            _ownsClient = false;
        }
        else
        {
            _client = reference.Region is { Length: > 0 } region
                ? new AmazonSimpleSystemsManagementClient(
                    new AmazonSimpleSystemsManagementConfig
                    {
                        RegionEndpoint = RegionEndpoint.GetBySystemName(region),
                    })
                : new AmazonSimpleSystemsManagementClient();
            _ownsClient = true;
        }
    }

    public AwsSsmReference Reference { get; }

    public string Description => $"ssm:{Reference.PathPrefix}";

    public async Task<EnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var values = new List<EnvironmentValue>();
        string? nextToken = null;

        do
        {
            var request = new GetParametersByPathRequest
            {
                Path = Reference.PathPrefix,
                Recursive = true,
                WithDecryption = false,
                NextToken = nextToken,
            };

            var response = await _client.GetParametersByPathAsync(request, cancellationToken).ConfigureAwait(false);

            foreach (var param in response.Parameters)
            {
                var key = ExtractKey(param.Name);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                // SecureString values are returned as encrypted ciphertext when WithDecryption=false.
                // Treat them as hidden to avoid surfacing useless garbled data.
                var value = param.Type == ParameterType.SecureString
                    ? EnvironmentValue.Hidden(key)
                    : EnvironmentValue.Available(key, param.Value);

                values.Add(value);
            }

            nextToken = response.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        return new EnvironmentSnapshot(Description, values);
    }

    public async Task<ProviderWriteResult> WriteAsync(
        IReadOnlyCollection<ResolvedEnvironmentValue> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var value in values)
        {
            EnvironmentKeyValidator.ThrowIfInvalid(value.Key);

            var request = new PutParameterRequest
            {
                // Normalised prefix already ends with '/', so we can concatenate directly.
                Name = _normalizedPrefix + value.Key,
                Value = value.Value,
                Type = value.Secret ? ParameterType.SecureString : ParameterType.String,
                Overwrite = true,
            };

            await _client.PutParameterAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return new ProviderWriteResult(values.Count);
    }

    /// <summary>
    /// Strips the path prefix from a fully-qualified parameter name to produce a bare env-var key.
    /// e.g. <c>/myapp/prod/DATABASE_URL</c> → <c>DATABASE_URL</c>.
    /// </summary>
    private string ExtractKey(string paramName) =>
        paramName.StartsWith(_normalizedPrefix, StringComparison.Ordinal)
            ? paramName[_normalizedPrefix.Length..]
            : throw new InvalidOperationException(
                $"Parameter name '{paramName}' does not start with the expected prefix '{_normalizedPrefix}'. " +
                "This indicates an unexpected SSM response. Verify the path prefix configured in the provider spec.");

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
