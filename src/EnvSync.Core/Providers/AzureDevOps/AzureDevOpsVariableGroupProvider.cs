using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnvSync.Core.Model;

namespace EnvSync.Core.Providers.AzureDevOps;

/// <summary>
/// Reads and writes Azure DevOps Library variable groups via the Azure DevOps REST API.
/// Secret variables are read as hidden because the API redacts their values.
/// </summary>
public sealed class AzureDevOpsVariableGroupProvider : IEnvironmentProvider, IDisposable
{
    private const string ApiVersion = "7.1";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly string _encodedPat;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates an Azure DevOps variable group provider.
    /// </summary>
    /// <param name="variableGroup">The variable group to read and write.</param>
    /// <param name="personalAccessToken">A personal access token with variable group permissions.</param>
    /// <param name="httpClient">An optional HTTP client for testing or advanced hosting scenarios.</param>
    public AzureDevOpsVariableGroupProvider(
        AzureDevOpsVariableGroupReference variableGroup,
        string personalAccessToken,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(variableGroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(personalAccessToken);

        VariableGroup = variableGroup;
        // Compute once; the encoded PAT does not change for the lifetime of this instance.
        _encodedPat = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{personalAccessToken}"));
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    /// <summary>
    /// Gets the Azure DevOps variable group reference.
    /// </summary>
    public AzureDevOpsVariableGroupReference VariableGroup { get; }

    /// <inheritdoc />
    public string Description => $"azuredevops:{VariableGroup}";

    /// <inheritdoc />
    public async Task<EnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var group = await GetVariableGroupAsync(cancellationToken).ConfigureAwait(false);
        var values = group.Variables.Select(pair =>
            pair.Value.IsSecret
                ? EnvironmentValue.Hidden(pair.Key)
                : EnvironmentValue.Available(pair.Key, pair.Value.Value));

        return new EnvironmentSnapshot(Description, values);
    }

    /// <inheritdoc />
    public async Task<ProviderWriteResult> WriteAsync(
        IReadOnlyCollection<ResolvedEnvironmentValue> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var group = await GetVariableGroupAsync(cancellationToken).ConfigureAwait(false);

        foreach (var value in values)
        {
            EnvironmentKeyValidator.ThrowIfInvalid(value.Key);

            group.Variables[value.Key] = new VariableValue
            {
                Value = value.Value,
                IsSecret = value.Secret,
            };
        }

        var updateUrl = BuildUrl(
            $"{Uri.EscapeDataString(VariableGroup.Project)}/_apis/distributedtask/variablegroups/{group.Id}");

        using var putResponse = await SendAsync(
            HttpMethod.Put,
            updateUrl,
            CreateJsonContent(new
            {
                id = group.Id,
                name = group.Name,
                type = group.Type,
                variables = group.Variables,
            }),
            cancellationToken).ConfigureAwait(false);

        return new ProviderWriteResult(values.Count);
    }

    private async Task<VariableGroupResponse> GetVariableGroupAsync(CancellationToken cancellationToken)
    {
        var listUrl = BuildUrl(
            $"{Uri.EscapeDataString(VariableGroup.Project)}/_apis/distributedtask/variablegroups" +
            $"?groupName={Uri.EscapeDataString(VariableGroup.GroupName)}&queryOrder=IdDescending&top=1");

        using var response = await SendAsync(HttpMethod.Get, listUrl, content: null, cancellationToken).ConfigureAwait(false);
        var result = await ReadJsonAsync<VariableGroupListResponse>(response, cancellationToken).ConfigureAwait(false);

        if (result.Count == 0 || result.Value is not [var group, ..])
        {
            throw new InvalidOperationException(
                $"Variable group '{VariableGroup.GroupName}' was not found in project '{VariableGroup.Project}'.");
        }

        return group;
    }

    /// <summary>
    /// Sends an authenticated request and returns the response. Throws on non-success status.
    /// The caller is responsible for disposing the returned response.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        Uri uri,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, uri, content);
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var statusCode = response.StatusCode;
            response.Dispose();
            throw new InvalidOperationException(
                $"Azure DevOps API {method.Method} {uri} failed ({(int)statusCode}): {body}");
        }

        return response;
    }

    /// <summary>
    /// Creates a request with all required headers set per-request so that an injected
    /// HttpClient is never mutated through DefaultRequestHeaders.
    /// </summary>
    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _encodedPat);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EnvSync", "1.0"));
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
               ?? throw new InvalidOperationException("Azure DevOps API returned an empty response body.");
    }

    /// <summary>
    /// Builds a fully-qualified Azure DevOps API URL, correctly appending the api-version
    /// query parameter regardless of whether the relative path already contains a query string.
    /// </summary>
    private Uri BuildUrl(string relativePath)
    {
        var separator = relativePath.Contains('?') ? '&' : '?';
        return new Uri(
            $"https://dev.azure.com/{Uri.EscapeDataString(VariableGroup.Organization)}/{relativePath}{separator}api-version={ApiVersion}",
            UriKind.Absolute);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed class VariableGroupListResponse
    {
        public int Count { get; set; }

        public List<VariableGroupResponse> Value { get; set; } = [];
    }

    private sealed class VariableGroupResponse
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = "Vsts";

        public Dictionary<string, VariableValue> Variables { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class VariableValue
    {
        [JsonPropertyName("value")]
        public string? Value { get; set; }

        [JsonPropertyName("isSecret")]
        public bool IsSecret { get; set; }
    }
}


