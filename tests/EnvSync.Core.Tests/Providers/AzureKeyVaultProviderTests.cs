using System.Net;
using System.Text;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Secrets;
using EnvSync.Core.Model;
using EnvSync.Core.Providers.AzureKeyVault;

namespace EnvSync.Core.Tests.Providers;

public sealed class AzureKeyVaultProviderTests
{
    // The vault name used across all tests.
    private static readonly AzureKeyVaultReference VaultRef = new("my-vault");
    private const string VaultBaseUri = "https://my-vault.vault.azure.net";

    // -------------------------------------------------------------------------
    // ReadAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReadAsync_ReturnsAvailableValue_ForEnabledSecret()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, ListBody([("MY-KEY", Enabled: true)]));
        handler.Enqueue(HttpStatusCode.OK, SecretBody("MY-KEY", "my-value"));

        var snapshot = await provider.ReadAsync();

        var value = snapshot.Values["MY_KEY"];
        Assert.Equal(ValueAvailability.Available, value.Availability);
        Assert.Equal("my-value", value.Value);
    }

    [Fact]
    public async Task ReadAsync_SkipsDisabledSecret()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, ListBody([("DISABLED-KEY", Enabled: false)]));

        var snapshot = await provider.ReadAsync();

        Assert.Empty(snapshot.Values);
    }

    [Fact]
    public async Task ReadAsync_ReturnsHiddenValue_WhenGetSecretReturns403()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, ListBody([("SECRET-KEY", Enabled: true)]));
        handler.Enqueue(HttpStatusCode.Forbidden, ErrorBody("Forbidden", "Caller does not have permission."));

        var snapshot = await provider.ReadAsync();

        var value = snapshot.Values["SECRET_KEY"];
        Assert.Equal(ValueAvailability.Hidden, value.Availability);
        Assert.Null(value.Value);
    }

    [Fact]
    public async Task ReadAsync_TranslatesHyphensInVaultNamesToUnderscores()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, ListBody([("DATABASE-URL", Enabled: true)]));
        handler.Enqueue(HttpStatusCode.OK, SecretBody("DATABASE-URL", "postgres://localhost"));

        var snapshot = await provider.ReadAsync();

        Assert.True(snapshot.Values.ContainsKey("DATABASE_URL"),
            "Key 'DATABASE_URL' expected after hyphen→underscore translation.");
        Assert.False(snapshot.Values.ContainsKey("DATABASE-URL"),
            "Raw hyphenated vault name must not be exposed.");
    }

    [Fact]
    public async Task ReadAsync_HandlesMixOfEnabledDisabledAndForbiddenSecrets()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, ListBody([
            ("VISIBLE", Enabled: true),
            ("GONE", Enabled: false),
            ("LOCKED", Enabled: true),
        ]));
        handler.Enqueue(HttpStatusCode.OK, SecretBody("VISIBLE", "yes"));
        handler.Enqueue(HttpStatusCode.Forbidden, ErrorBody("Forbidden", "Access denied."));

        var snapshot = await provider.ReadAsync();

        Assert.Equal(2, snapshot.Values.Count);
        Assert.Equal(ValueAvailability.Available, snapshot.Values["VISIBLE"].Availability);
        Assert.Equal(ValueAvailability.Hidden, snapshot.Values["LOCKED"].Availability);
        Assert.False(snapshot.Values.ContainsKey("GONE"));
    }

    [Fact]
    public async Task ReadAsync_ReturnsBothPages_WhenListResponseHasNextLink()
    {
        var (provider, handler) = Build();
        var apiVersion = "2025-07-01";

        // The FIFO queue serves requests in order; URL routes serve before the queue.
        // Sequence:
        //   1. GET /secrets/?api-version=... → page 1 list (from FIFO queue)
        //   2. GET /secrets/PAGE1-KEY/...    → secret body  (URL route)
        //   3. GET .../skiptoken=abc...      → page 2 list  (URL route)
        //   4. GET /secrets/PAGE2-KEY/...    → secret body  (URL route)

        // Page 1 list — served first via FIFO.
        handler.Enqueue(HttpStatusCode.OK,
            $"{{\"value\":[{{\"id\":\"{VaultBaseUri}/secrets/PAGE1-KEY\"," +
            $"\"attributes\":{{\"enabled\":true}}}}]," +
            $"\"nextLink\":\"{VaultBaseUri}/secrets/?api-version={apiVersion}&$skiptoken=abc\"}}");

        // URL-routed responses for get-secret and page 2 list.
        handler.EnqueueForUrl("secrets/PAGE1-KEY", HttpStatusCode.OK, SecretBody("PAGE1-KEY", "value-from-page-1"));
        handler.EnqueueForUrl("skiptoken=abc", HttpStatusCode.OK,
            $"{{\"value\":[{{\"id\":\"{VaultBaseUri}/secrets/PAGE2-KEY\"," +
            $"\"attributes\":{{\"enabled\":true}}}}]}}");
        handler.EnqueueForUrl("secrets/PAGE2-KEY", HttpStatusCode.OK, SecretBody("PAGE2-KEY", "value-from-page-2"));

        var snapshot = await provider.ReadAsync();

        Assert.Equal(2, snapshot.Values.Count);
        Assert.True(snapshot.Values.ContainsKey("PAGE1_KEY"), "PAGE1_KEY should be present after hyphen→underscore.");
        Assert.True(snapshot.Values.ContainsKey("PAGE2_KEY"), "PAGE2_KEY should be present from the second page.");
        Assert.Equal("value-from-page-1", snapshot.Values["PAGE1_KEY"].Value);
        Assert.Equal("value-from-page-2", snapshot.Values["PAGE2_KEY"].Value);
    }

    // -------------------------------------------------------------------------
    // WriteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WriteAsync_TranslatesUnderscoresToHyphens()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, SecretBody("DATABASE-URL", "postgres://localhost"));

        await provider.WriteAsync([new ResolvedEnvironmentValue("DATABASE_URL", "postgres://localhost", false)]);

        // Verify the PUT was sent to the hyphenated secret name.
        Assert.Single(handler.SentRequests, r => r.RequestUri!.AbsolutePath == "/secrets/DATABASE-URL");
    }

    [Fact]
    public async Task WriteAsync_CallsSetSecretForEachValue()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, SecretBody("API-KEY", "k1"));
        handler.Enqueue(HttpStatusCode.OK, SecretBody("APP-ENV", "prod"));

        var result = await provider.WriteAsync([
            new ResolvedEnvironmentValue("API_KEY", "k1", true),
            new ResolvedEnvironmentValue("APP_ENV", "prod", false),
        ]);

        Assert.Equal(2, result.UpdatedCount);
        Assert.Equal(2, handler.SentRequests.Count);
        Assert.Contains(handler.SentRequests, r => r.RequestUri!.AbsolutePath == "/secrets/API-KEY");
        Assert.Contains(handler.SentRequests, r => r.RequestUri!.AbsolutePath == "/secrets/APP-ENV");
    }

    [Fact]
    public async Task WriteAsync_ReturnsUpdatedCountMatchingValuesWritten()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, SecretBody("K1", "v1"));
        handler.Enqueue(HttpStatusCode.OK, SecretBody("K2", "v2"));
        handler.Enqueue(HttpStatusCode.OK, SecretBody("K3", "v3"));

        var result = await provider.WriteAsync([
            new ResolvedEnvironmentValue("K1", "v1", false),
            new ResolvedEnvironmentValue("K2", "v2", false),
            new ResolvedEnvironmentValue("K3", "v3", false),
        ]);

        Assert.Equal(3, result.UpdatedCount);
    }

    // -------------------------------------------------------------------------
    // Description
    // -------------------------------------------------------------------------

    [Fact]
    public void Description_IncludesVaultName()
    {
        var (provider, _) = Build(vaultName: "acme-vault");
        Assert.Equal("azurekeyvault:acme-vault", provider.Description);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a provider backed by a queued HTTP handler. Uses a real <see cref="SecretClient"/>
    /// with <see cref="HttpClientTransport"/> so we test the HTTP contract directly rather than
    /// relying on subclassing (which is unreliable in Azure SDK 4.10+).
    /// </summary>
    private static (AzureKeyVaultProvider Provider, QueuedHttpMessageHandler Handler) Build(
        string vaultName = "my-vault")
    {
        var handler = new QueuedHttpMessageHandler();
        var options = new SecretClientOptions
        {
            Transport = new HttpClientTransport(new HttpClient(handler)),
        };
        options.Retry.MaxRetries = 0;
        var vaultRef = new AzureKeyVaultReference(vaultName);
        var client = new SecretClient(new Uri(vaultRef.VaultUri), new FakeTokenCredential(), options);
        return (new AzureKeyVaultProvider(vaultRef, client), handler);
    }

    private static string ListBody(IEnumerable<(string Name, bool Enabled)> secrets)
    {
        var items = secrets.Select(s =>
            $"{{\"id\":\"{VaultBaseUri}/secrets/{s.Name}\"," +
            $"\"attributes\":{{\"enabled\":{s.Enabled.ToString().ToLowerInvariant()}}}}}");
        return $"{{\"value\":[{string.Join(",", items)}]}}";
    }

    private static string SecretBody(string name, string value) =>
        $"{{\"value\":\"{value}\"," +
        $"\"id\":\"{VaultBaseUri}/secrets/{name}/abcdef\"," +
        $"\"attributes\":{{\"enabled\":true}}}}";

    private static string ErrorBody(string code, string message) =>
        $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"{message}\"}}}}";

    /// <summary>
    /// A <see cref="TokenCredential"/> that returns a pre-built fake token immediately,
    /// so no HTTP round-trip is needed for authentication.
    /// </summary>
    private sealed class FakeTokenCredential : TokenCredential
    {
        private static readonly AccessToken FakeToken =
            new("fake-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => FakeToken;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(FakeToken);
    }

    private sealed class QueuedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _queue = new();
        private readonly List<(string UrlPattern, HttpStatusCode Status, string Body)> _urlRoutes = [];

        public List<HttpRequestMessage> SentRequests { get; } = [];

        public void Enqueue(HttpStatusCode status, string body) => _queue.Enqueue((status, body));

        /// <summary>Enqueues a response that is only served when the request URL contains <paramref name="urlPattern"/>.</summary>
        public void EnqueueForUrl(string urlPattern, HttpStatusCode status, string body) =>
            _urlRoutes.Add((urlPattern, status, body));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SentRequests.Add(request);

            var url = request.RequestUri?.ToString() ?? string.Empty;

            // Try URL-routed responses first.
            for (int i = 0; i < _urlRoutes.Count; i++)
            {
                if (url.Contains(_urlRoutes[i].UrlPattern, StringComparison.OrdinalIgnoreCase))
                {
                    var (_, status, body) = _urlRoutes[i];
                    _urlRoutes.RemoveAt(i);
                    return Task.FromResult(new HttpResponseMessage(status)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    });
                }
            }

            // Fall back to FIFO queue.
            var (qStatus, qBody) = _queue.Dequeue();
            return Task.FromResult(new HttpResponseMessage(qStatus)
            {
                Content = new StringContent(qBody, Encoding.UTF8, "application/json"),
            });
        }
    }
}

