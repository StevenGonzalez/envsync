using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using EnvSync.Core.Model;
using EnvSync.Core.Providers.AzureAppService;

namespace EnvSync.Core.Tests.Providers;

public sealed class AzureAppServiceProviderTests
{
    private const string SubscriptionId = "00000000-0000-0000-0000-000000000001";

    // ReadAsync

    [Fact]
    public async Task ReadAsync_ReturnsApplicationSettings()
    {
        var (provider, handler, credential) = Build();
        handler.Enqueue(HttpStatusCode.OK, AppSettingsBody(("APP_ENV", "production"), ("DATABASE_URL", "postgres://localhost")));

        var snapshot = await provider.ReadAsync();

        Assert.Equal("azureappservice:00000000-0000-0000-0000-000000000001/my-rg/my-api", snapshot.SourceName);
        Assert.Equal("production", snapshot.Values["APP_ENV"].Value);
        Assert.Equal("postgres://localhost", snapshot.Values["DATABASE_URL"].Value);

        var request = Assert.Single(handler.SentRequests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/my-rg/providers/Microsoft.Web/sites/my-api/config/appsettings/list",
            request.RequestUri.AbsolutePath);
        Assert.Equal("api-version=2025-05-01", request.RequestUri.Query.TrimStart('?'));
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal("fake-arm-token", request.Authorization?.Parameter);
        Assert.Equal("https://management.azure.com/.default", Assert.Single(credential.LastScopes));
    }

    [Fact]
    public async Task ReadAsync_UsesSlotEndpoint_WhenSlotNameIsPresent()
    {
        var reference = new AzureAppServiceReference(SubscriptionId, "my-rg", "my-api", "staging");
        var (provider, handler, _) = Build(reference);
        handler.Enqueue(HttpStatusCode.OK, AppSettingsBody(("APP_ENV", "staging")));

        var snapshot = await provider.ReadAsync();

        Assert.Equal("staging", snapshot.Values["APP_ENV"].Value);
        Assert.Equal(
            "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/my-rg/providers/Microsoft.Web/sites/my-api/slots/staging/config/appsettings/list",
            Assert.Single(handler.SentRequests).RequestUri.AbsolutePath);
    }

    // WriteAsync

    [Fact]
    public async Task WriteAsync_MergesWithExistingSettings_AndPutsFullDictionary()
    {
        var (provider, handler, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, AppSettingsBody(("EXISTING", "keep"), ("APP_ENV", "old")));
        handler.Enqueue(HttpStatusCode.OK, AppSettingsBody(("EXISTING", "keep"), ("APP_ENV", "production"), ("API_KEY", "secret")));

        var result = await provider.WriteAsync([
            new ResolvedEnvironmentValue("APP_ENV", "production", false),
            new ResolvedEnvironmentValue("API_KEY", "secret", true),
        ]);

        Assert.Equal(2, result.UpdatedCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("restarts the app", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, handler.SentRequests.Count);

        var putRequest = handler.SentRequests[1];
        Assert.Equal(HttpMethod.Put, putRequest.Method);
        Assert.Equal(
            "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/my-rg/providers/Microsoft.Web/sites/my-api/config/appsettings",
            putRequest.RequestUri.AbsolutePath);

        var properties = ReadProperties(putRequest.Body!);
        Assert.Equal("keep", properties["EXISTING"]);
        Assert.Equal("production", properties["APP_ENV"]);
        Assert.Equal("secret", properties["API_KEY"]);
    }

    [Fact]
    public async Task WriteAsync_WithNoValues_DoesNotCallAzure()
    {
        var (provider, handler, _) = Build();

        var result = await provider.WriteAsync([]);

        Assert.Equal(0, result.UpdatedCount);
        Assert.Empty(result.Warnings);
        Assert.Empty(handler.SentRequests);
    }

    [Fact]
    public async Task WriteAsync_ThrowsForInvalidEnvironmentKey()
    {
        var (provider, handler, _) = Build();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.WriteAsync([new ResolvedEnvironmentValue("not-valid", "value", false)]));

        Assert.Contains("environment variable name", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.SentRequests);
    }

    // Reference

    [Fact]
    public void Reference_ToString_IncludesSlot_WhenSlotNameIsPresent()
    {
        var reference = new AzureAppServiceReference(SubscriptionId, "my-rg", "my-api", "staging");

        Assert.Equal("00000000-0000-0000-0000-000000000001/my-rg/my-api/staging", reference.ToString());
    }

    [Fact]
    public void Reference_RejectsInvalidSubscriptionId()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new AzureAppServiceReference("not-a-guid", "my-rg", "my-api"));

        Assert.Contains("GUID", exception.Message, StringComparison.Ordinal);
    }

    // Helpers

    private static (AzureAppServiceProvider Provider, QueuedHttpMessageHandler Handler, FakeTokenCredential Credential) Build(
        AzureAppServiceReference? reference = null)
    {
        var handler = new QueuedHttpMessageHandler();
        var credential = new FakeTokenCredential();
        var provider = new AzureAppServiceProvider(
            reference ?? new AzureAppServiceReference(SubscriptionId, "my-rg", "my-api"),
            credential,
            new HttpClient(handler));

        return (provider, handler, credential);
    }

    private static string AppSettingsBody(params (string Key, string? Value)[] settings)
    {
        var properties = settings.ToDictionary(static setting => setting.Key, static setting => setting.Value);
        return JsonSerializer.Serialize(new { properties });
    }

    private static Dictionary<string, string?> ReadProperties(string body)
    {
        using var document = JsonDocument.Parse(body);
        var properties = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var property in document.RootElement.GetProperty("properties").EnumerateObject())
        {
            properties[property.Name] = property.Value.ValueKind == JsonValueKind.Null
                ? null
                : property.Value.GetString();
        }

        return properties;
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        private static readonly AccessToken FakeToken =
            new("fake-arm-token", DateTimeOffset.UtcNow.AddHours(1));

        public IReadOnlyList<string> LastScopes { get; private set; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            LastScopes = requestContext.Scopes;
            return FakeToken;
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            LastScopes = requestContext.Scopes;
            return ValueTask.FromResult(FakeToken);
        }
    }

    private sealed class QueuedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _queue = new();

        public List<SentRequest> SentRequests { get; } = [];

        public void Enqueue(HttpStatusCode status, string body) => _queue.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            SentRequests.Add(new SentRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization,
                body));

            var (status, responseBody) = _queue.Dequeue();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record SentRequest(
        HttpMethod Method,
        Uri RequestUri,
        AuthenticationHeaderValue? Authorization,
        string? Body);
}
