using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using EnvSync.Core.Model;

namespace EnvSync.Core.Providers.AzureAppService;

/// <summary>
/// Reads and writes Azure App Service application settings through Azure Resource Manager.
/// <para>
/// App Service exposes application settings to app code as environment variables at runtime.
/// Updating application settings restarts the app.
/// </para>
/// <para>
/// Authentication is handled by <see cref="DefaultAzureCredential"/>, which supports local Azure CLI
/// sign-in, managed identity, workload identity, and service principal environment variables.
/// </para>
/// </summary>
public sealed class AzureAppServiceProvider : IEnvironmentProvider, IDisposable
{
    private const string ApiVersion = "2025-05-01";
    private const string ArmEndpoint = "https://management.azure.com";
    private const string ArmScope = "https://management.azure.com/.default";
    private const string RestartWarning =
        "Azure App Service restarts the app when application settings are changed.";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly TokenCredential _credential;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates an Azure App Service provider.
    /// </summary>
    /// <param name="reference">The App Service app or slot reference.</param>
    /// <param name="credential">An optional Azure credential. Defaults to <see cref="DefaultAzureCredential"/>.</param>
    /// <param name="httpClient">An optional HTTP client for testing or advanced hosting scenarios.</param>
    public AzureAppServiceProvider(
        AzureAppServiceReference reference,
        TokenCredential? credential = null,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(reference);

        Reference = reference;
        _credential = credential ?? new DefaultAzureCredential();
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    /// <summary>
    /// Gets the App Service reference.
    /// </summary>
    public AzureAppServiceReference Reference { get; }

    /// <inheritdoc />
    public string Description => $"azureappservice:{Reference}";

    /// <inheritdoc />
    public async Task<EnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var values = settings
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

        if (values.Count == 0)
        {
            return new ProviderWriteResult(0);
        }

        foreach (var value in values)
        {
            EnvironmentKeyValidator.ThrowIfInvalid(value.Key);
        }

        var settings = await ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var value in values)
        {
            settings[value.Key] = value.Value;
        }

        using var response = await SendAsync(
            HttpMethod.Put,
            $"{AppSettingsPath}?api-version={ApiVersion}",
            CreateJsonContent(new AppSettingsPayload { Properties = settings }),
            cancellationToken).ConfigureAwait(false);

        return new ProviderWriteResult(values.Count, [RestartWarning]);
    }

    private async Task<Dictionary<string, string?>> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"{AppSettingsPath}/list?api-version={ApiVersion}",
            content: null,
            cancellationToken).ConfigureAwait(false);

        var payload = await ReadJsonAsync<AppSettingsPayload>(response, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, string?>(payload.Properties, StringComparer.Ordinal);
    }

    private string AppSettingsPath
    {
        get
        {
            var slotSegment = Reference.SlotName is null
                ? string.Empty
                : $"/slots/{Escape(Reference.SlotName)}";

            return $"/subscriptions/{Escape(Reference.SubscriptionId)}" +
                   $"/resourceGroups/{Escape(Reference.ResourceGroupName)}" +
                   $"/providers/Microsoft.Web/sites/{Escape(Reference.AppName)}" +
                   $"{slotSegment}/config/appsettings";
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string pathAndQuery,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(method, pathAndQuery, content, cancellationToken).ConfigureAwait(false);
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var statusCode = response.StatusCode;
            response.Dispose();
            throw new InvalidOperationException(
                $"Azure App Service API {method.Method} {pathAndQuery} failed ({(int)statusCode}): {body}");
        }

        return response;
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        string pathAndQuery,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext([ArmScope]),
            cancellationToken).ConfigureAwait(false);

        var request = new HttpRequestMessage(method, new Uri($"{ArmEndpoint}{pathAndQuery}", UriKind.Absolute))
        {
            Content = content,
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EnvSync", "1.0"));
        return request;
    }

    private static StringContent CreateJsonContent<T>(T payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Azure App Service API returned an empty response body.");
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed class AppSettingsPayload
    {
        [JsonPropertyName("properties")]
        public Dictionary<string, string?> Properties { get; set; } = new(StringComparer.Ordinal);
    }
}
