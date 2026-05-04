using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnvSync.Core.Model;

namespace EnvSync.Core.Providers.Vault;

/// <summary>
/// Reads and writes environment variables stored as key-value pairs in a single HashiCorp Vault
/// KV v2 secret. All keys within the secret surface as individual environment values.
/// <para>
/// Because Vault KV v2 does not distinguish secret vs. non-secret at the individual key level,
/// all values are returned as <see cref="EnvironmentValue.Available"/>.
/// </para>
/// <para>
/// Writes use a read-merge-write strategy: the existing secret data is fetched first, the new
/// values are merged in, and the result is written back in a single API call. This preserves
/// keys not present in the current write batch.
/// </para>
/// </summary>
public sealed class VaultKvProvider : IEnvironmentProvider, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly string _token;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates a HashiCorp Vault KV v2 provider.
    /// </summary>
    /// <param name="reference">The Vault address, mount, and secret path.</param>
    /// <param name="token">A Vault token with read and write access to the secret path.</param>
    /// <param name="httpClient">An optional HTTP client for testing or advanced hosting scenarios.</param>
    public VaultKvProvider(VaultKvReference reference, string token, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        Reference = reference;
        _token = token;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    /// <summary>
    /// Gets the Vault KV reference.
    /// </summary>
    public VaultKvReference Reference { get; }

    /// <inheritdoc />
    public string Description => $"vault:{Reference}";

    /// <inheritdoc />
    public async Task<EnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            DataPath,
            content: null,
            cancellationToken).ConfigureAwait(false);

        var payload = await ReadJsonAsync<KvV2ReadResponse>(response, cancellationToken).ConfigureAwait(false);

        var values = payload.Data.Data
            .Select(static pair => EnvironmentValue.Available(pair.Key, pair.Value))
            .ToArray();

        return new EnvironmentSnapshot(Description, values);
    }

    /// <inheritdoc />
    public async Task<ProviderWriteResult> WriteAsync(
        IReadOnlyCollection<ResolvedEnvironmentValue> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        // Fetch existing data first so that keys not in the current batch are preserved.
        var merged = await ReadExistingDataAsync(cancellationToken).ConfigureAwait(false);

        foreach (var value in values)
        {
            EnvironmentKeyValidator.ThrowIfInvalid(value.Key);
            merged[value.Key] = value.Value;
        }

        using var writeResponse = await SendAsync(
            HttpMethod.Post,
            DataPath,
            CreateJsonContent(new { data = merged }),
            cancellationToken).ConfigureAwait(false);

        return new ProviderWriteResult(values.Count);
    }

    /// <summary>
    /// Reads the current secret data, returning an empty dictionary when the path does not yet exist.
    /// </summary>
    private async Task<Dictionary<string, string?>> ReadExistingDataAsync(CancellationToken cancellationToken)
    {
        using var response = await TrySendAsync(HttpMethod.Get, DataPath, cancellationToken).ConfigureAwait(false);

        if (response is null)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        var payload = await ReadJsonAsync<KvV2ReadResponse>(response, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, string?>(payload.Data.Data, StringComparer.Ordinal);
    }

    // The KV v2 data endpoint for the configured mount and path.
    private string DataPath => $"/v1/{EscapePathSegment(Reference.Mount)}/data/{EscapePath(Reference.SecretPath)}";

    /// <summary>
    /// Sends an authenticated request and returns the response. Throws on non-success status.
    /// The caller is responsible for disposing the returned response.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, content);
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var statusCode = response.StatusCode;
            response.Dispose();
            throw new InvalidOperationException(
                $"Vault API {method.Method} {path} failed ({(int)statusCode}): {body}");
        }

        return response;
    }

    /// <summary>
    /// Like <see cref="SendAsync"/> but returns <see langword="null"/> on 404 instead of throwing.
    /// Used when the absence of a path is a valid, expected state.
    /// </summary>
    private async Task<HttpResponseMessage?> TrySendAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, content: null);
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var statusCode = response.StatusCode;
            response.Dispose();
            throw new InvalidOperationException(
                $"Vault API {method.Method} {path} failed ({(int)statusCode}): {body}");
        }

        return response;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var uri = new Uri($"{Reference.Address.TrimEnd('/')}{path}", UriKind.Absolute);
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Add("X-Vault-Token", _token);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EnvSync", "1.0"));
        return request;
    }

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(EscapePathSegment));

    private static string EscapePathSegment(string segment) => Uri.EscapeDataString(segment);

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
               ?? throw new InvalidOperationException("Vault API returned an empty response body.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed class KvV2ReadResponse
    {
        public KvV2ResponseData Data { get; set; } = new();
    }

    private sealed class KvV2ResponseData
    {
        public Dictionary<string, string?> Data { get; set; } = new(StringComparer.Ordinal);
    }
}
