using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnvSync.Core.Model;
using Sodium;

namespace EnvSync.Core.Providers.GitHub;

/// <summary>
/// Reads GitHub Actions variables and secrets and writes values back to repository-level settings.
/// </summary>
public sealed class GitHubActionsProvider : IEnvironmentProvider, IDisposable
{
    private const string ApiVersion = "2022-11-28";
    private const int PageSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly string _token;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates a GitHub Actions provider for a repository.
    /// </summary>
    /// <param name="repository">The GitHub repository to read and write.</param>
    /// <param name="token">A GitHub token with Actions variable and secret permissions.</param>
    /// <param name="httpClient">An optional HTTP client for testing or advanced hosting scenarios.</param>
    public GitHubActionsProvider(GitHubRepositoryReference repository, string token, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        Repository = repository;
        _token = token;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    /// <summary>
    /// Gets the GitHub repository reference.
    /// </summary>
    public GitHubRepositoryReference Repository { get; }

    /// <inheritdoc />
    public string Description => $"github:{Repository}";

    // Convenience property to avoid repeating the owner/repo path in every endpoint string.
    private string RepoPath =>
        $"{Uri.EscapeDataString(Repository.Owner)}/{Uri.EscapeDataString(Repository.Repository)}";

    /// <inheritdoc />
    public async Task<EnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var variables = await GetVariablesAsync(cancellationToken).ConfigureAwait(false);
        var secrets = await GetSecretsAsync(cancellationToken).ConfigureAwait(false);

        var values = new List<EnvironmentValue>(variables.Count + secrets.Count);
        values.AddRange(variables.Select(static pair => EnvironmentValue.Available(pair.Key, pair.Value)));

        foreach (var secretName in secrets)
        {
            if (!variables.ContainsKey(secretName))
            {
                values.Add(EnvironmentValue.Hidden(secretName));
            }
        }

        return new EnvironmentSnapshot(Description, values.OrderBy(static value => value.Key, StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public async Task<ProviderWriteResult> WriteAsync(IReadOnlyCollection<ResolvedEnvironmentValue> values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var updatedCount = 0;
        RepositorySecretPublicKeyResponse? publicKey = null;
        foreach (var value in values)
        {
            EnvironmentKeyValidator.ThrowIfInvalid(value.Key);

            if (value.Secret)
            {
                publicKey ??= await GetSecretPublicKeyAsync(cancellationToken).ConfigureAwait(false);
                await UpsertSecretAsync(value.Key, value.Value, publicKey, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await UpsertVariableAsync(value.Key, value.Value, cancellationToken).ConfigureAwait(false);
            }

            updatedCount++;
        }

        return new ProviderWriteResult(updatedCount);
    }

    private async Task<Dictionary<string, string>> GetVariablesAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var page = 1; ; page++)
        {
            using var response = await SendAsync(
                HttpMethod.Get,
                $"/repos/{RepoPath}/actions/variables?per_page={PageSize}&page={page}",
                content: null,
                cancellationToken).ConfigureAwait(false);

            var payload = await ReadJsonAsync<RepositoryVariablesResponse>(response, cancellationToken).ConfigureAwait(false);
            var items = payload.Variables;

            foreach (var variable in items)
            {
                if (!string.IsNullOrWhiteSpace(variable.Name))
                {
                    result[variable.Name] = variable.Value ?? string.Empty;
                }
            }

            if (items.Count < PageSize)
            {
                return result;
            }
        }
    }

    private async Task<HashSet<string>> GetSecretsAsync(CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        for (var page = 1; ; page++)
        {
            using var response = await SendAsync(
                HttpMethod.Get,
                $"/repos/{RepoPath}/actions/secrets?per_page={PageSize}&page={page}",
                content: null,
                cancellationToken).ConfigureAwait(false);

            var payload = await ReadJsonAsync<RepositorySecretsResponse>(response, cancellationToken).ConfigureAwait(false);
            var items = payload.Secrets;

            foreach (var secret in items)
            {
                if (!string.IsNullOrWhiteSpace(secret.Name))
                {
                    _ = result.Add(secret.Name);
                }
            }

            if (items.Count < PageSize)
            {
                return result;
            }
        }
    }

    private async Task UpsertVariableAsync(string name, string value, CancellationToken cancellationToken)
    {
        // Try PATCH first. GitHub returns 204 on success and 404 when the variable does not exist yet.
        using var patchRequest = CreateRequest(
            HttpMethod.Patch,
            $"/repos/{RepoPath}/actions/variables/{Uri.EscapeDataString(name)}",
            CreateJsonContent(new { name, value }));

        using var patchResponse = await _httpClient.SendAsync(patchRequest, cancellationToken).ConfigureAwait(false);

        if (patchResponse.IsSuccessStatusCode)
        {
            return;
        }

        if (patchResponse.StatusCode == HttpStatusCode.NotFound)
        {
            using var createResponse = await SendAsync(
                HttpMethod.Post,
                $"/repos/{RepoPath}/actions/variables",
                CreateJsonContent(new { name, value }),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var errorBody = await patchResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"GitHub API request failed ({(int)patchResponse.StatusCode}): {errorBody}");
    }

    private async Task<RepositorySecretPublicKeyResponse> GetSecretPublicKeyAsync(CancellationToken cancellationToken)
    {
        using var keyResponse = await SendAsync(
            HttpMethod.Get,
            $"/repos/{RepoPath}/actions/secrets/public-key",
            content: null,
            cancellationToken).ConfigureAwait(false);

        return await ReadJsonAsync<RepositorySecretPublicKeyResponse>(keyResponse, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertSecretAsync(
        string name,
        string value,
        RepositorySecretPublicKeyResponse publicKey,
        CancellationToken cancellationToken)
    {
        var encryptedValue = Convert.ToBase64String(
            SealedPublicKeyBox.Create(Encoding.UTF8.GetBytes(value), Convert.FromBase64String(publicKey.Key)));

        using var putResponse = await SendAsync(
            HttpMethod.Put,
            $"/repos/{RepoPath}/actions/secrets/{Uri.EscapeDataString(name)}",
            CreateJsonContent(new { encrypted_value = encryptedValue, key_id = publicKey.KeyId }),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends an authenticated request and returns the response. Throws on non-success status.
    /// The caller is responsible for disposing the returned response.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, relativePath, content);
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var statusCode = response.StatusCode;
            response.Dispose();
            throw new InvalidOperationException(
                $"GitHub API {method.Method} {relativePath} failed ({(int)statusCode}): {body}");
        }

        return response;
    }

    /// <summary>
    /// Creates a request with all required GitHub headers set per-request so that an injected
    /// HttpClient is never mutated through DefaultRequestHeaders.
    /// </summary>
    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, BuildUri(relativePath)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EnvSync", "1.0"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        return request;
    }

    private static StringContent CreateJsonContent<T>(T payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    /// <summary>
    /// Deserializes the response body as JSON. Does not dispose the response;
    /// the caller manages its lifetime via <c>using</c>.
    /// </summary>
    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("GitHub API returned an empty response body.");
    }

    private Uri BuildUri(string relativePath) => new($"https://api.github.com{relativePath}", UriKind.Absolute);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed class RepositoryVariablesResponse
    {
        public List<RepositoryVariableItem> Variables { get; set; } = [];
    }

    private sealed class RepositoryVariableItem
    {
        public string Name { get; set; } = string.Empty;

        public string? Value { get; set; }
    }

    private sealed class RepositorySecretsResponse
    {
        public List<RepositorySecretItem> Secrets { get; set; } = [];
    }

    private sealed class RepositorySecretItem
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class RepositorySecretPublicKeyResponse
    {
        // GitHub returns snake_case; PropertyNameCaseInsensitive only bridges case differences,
        // not underscores, so an explicit attribute is required here.
        [JsonPropertyName("key_id")]
        public string KeyId { get; set; } = string.Empty;

        public string Key { get; set; } = string.Empty;
    }
}


