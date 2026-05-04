using System.Net;
using System.Text;
using System.Text.Json;
using EnvSync.Core.Model;
using EnvSync.Core.Providers.Vault;

namespace EnvSync.Core.Tests.Providers;

public sealed class VaultKvProviderTests
{
    private static readonly VaultKvReference Reference = new("http://127.0.0.1:8200", "secret", "myapp/production");
    private const string Token = "s.test-token";

    // ReadAsync

    [Fact]
    public async Task ReadAsync_ReturnsAllKeyValuesAsAvailable()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VaultReadBody(new Dictionary<string, string>
        {
            ["DATABASE_URL"] = "postgres://localhost/mydb",
            ["PORT"] = "5432",
        }));

        using var provider = new VaultKvProvider(Reference, Token, new HttpClient(handler));
        var snapshot = await provider.ReadAsync();

        Assert.Equal(2, snapshot.Values.Count);
        Assert.Equal("postgres://localhost/mydb", snapshot.Values["DATABASE_URL"].Value);
        Assert.Equal(ValueAvailability.Available, snapshot.Values["DATABASE_URL"].Availability);
        Assert.Equal("5432", snapshot.Values["PORT"].Value);
    }

    [Fact]
    public async Task ReadAsync_SendsVaultTokenHeader()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VaultReadBody(new Dictionary<string, string>
        {
            ["KEY"] = "value",
        }));

        using var provider = new VaultKvProvider(Reference, Token, new HttpClient(handler));
        await provider.ReadAsync();

        var request = handler.SentRequests[0];
        Assert.True(request.Headers.TryGetValues("X-Vault-Token", out var values));
        Assert.Contains(Token, values);
    }

    [Fact]
    public async Task ReadAsync_TargetsCorrectVaultDataPath()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VaultReadBody([]));

        using var provider = new VaultKvProvider(Reference, Token, new HttpClient(handler));
        await provider.ReadAsync();

        Assert.Equal("/v1/secret/data/myapp/production", handler.SentRequests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ReadAsync_EscapesVaultPathSegments()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VaultReadBody([]));
        var reference = new VaultKvReference("http://127.0.0.1:8200", "secret mount", "folder name/key#1");

        using var provider = new VaultKvProvider(reference, Token, new HttpClient(handler));
        await provider.ReadAsync();

        Assert.Equal("/v1/secret%20mount/data/folder%20name/key%231", handler.SentRequests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ReadAsync_ThrowsWithStatusAndBodyOnFailure()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Forbidden, """{"errors":["permission denied"]}""");

        using var provider = new VaultKvProvider(Reference, Token, new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ReadAsync());

        Assert.Contains("403", exception.Message, StringComparison.Ordinal);
        Assert.Contains("permission denied", exception.Message, StringComparison.Ordinal);
    }

    // WriteAsync

    [Fact]
    public async Task WriteAsync_MergesNewValuesWithExistingSecret()
    {
        // Arrange: the existing secret has EXISTING_KEY; we're writing NEW_KEY.
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VaultReadBody(new Dictionary<string, string>
        {
            ["EXISTING_KEY"] = "old-value",
        }));
        handler.Enqueue(HttpStatusCode.OK, "{}");

        using var provider = new VaultKvProvider(Reference, Token, new HttpClient(handler));
        await provider.WriteAsync([new ResolvedEnvironmentValue("NEW_KEY", "new-value", false)]);

        // The POST body must contain both the preserved existing key and the new one.
        var postBody = handler.SentBodies[1]!;
        var doc = JsonDocument.Parse(postBody);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("old-value", data.GetProperty("EXISTING_KEY").GetString());
        Assert.Equal("new-value", data.GetProperty("NEW_KEY").GetString());
    }

    [Fact]
    public async Task WriteAsync_OverwritesExistingKeyWhenPresentInBatch()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VaultReadBody(new Dictionary<string, string>
        {
            ["PORT"] = "3000",
        }));
        handler.Enqueue(HttpStatusCode.OK, "{}");

        using var provider = new VaultKvProvider(Reference, Token, new HttpClient(handler));
        await provider.WriteAsync([new ResolvedEnvironmentValue("PORT", "8080", false)]);

        var postBody = handler.SentBodies[1]!;
        var data = JsonDocument.Parse(postBody).RootElement.GetProperty("data");
        Assert.Equal("8080", data.GetProperty("PORT").GetString());
    }

    [Fact]
    public async Task WriteAsync_CreatesNewSecret_WhenPathDoesNotExist()
    {
        // Arrange: GET returns 404 (secret not yet created); POST should succeed.
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");
        handler.Enqueue(HttpStatusCode.OK, "{}");

        using var provider = new VaultKvProvider(Reference, Token, new HttpClient(handler));
        await provider.WriteAsync([new ResolvedEnvironmentValue("FIRST_KEY", "hello", false)]);

        // Only the new key should be in the POST body (no existing keys to merge with).
        var postBody = handler.SentBodies[1]!;
        var data = JsonDocument.Parse(postBody).RootElement.GetProperty("data");
        Assert.Equal("hello", data.GetProperty("FIRST_KEY").GetString());
    }

    [Fact]
    public async Task WriteAsync_ReturnsUpdatedCountMatchingValuesWritten()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");
        handler.Enqueue(HttpStatusCode.OK, "{}");

        using var provider = new VaultKvProvider(Reference, Token, new HttpClient(handler));
        var result = await provider.WriteAsync([
            new ResolvedEnvironmentValue("K1", "v1", false),
            new ResolvedEnvironmentValue("K2", "v2", false),
        ]);

        Assert.Equal(2, result.UpdatedCount);
    }

    [Fact]
    public async Task WriteAsync_HandlesNullValuedExistingKeys_Gracefully()
    {
        // Vault returns null for destroyed-but-not-deleted versions; the merge must not throw.
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, NullableVaultReadBody(new Dictionary<string, string?>
        {
            ["DESTROYED_KEY"] = null,
            ["ALIVE_KEY"] = "alive",
        }));
        handler.Enqueue(HttpStatusCode.OK, "{}");

        using var provider = new VaultKvProvider(Reference, Token, new HttpClient(handler));
        var result = await provider.WriteAsync([new ResolvedEnvironmentValue("NEW_KEY", "new-value", false)]);

        Assert.Equal(1, result.UpdatedCount);

        // The POST body must include all keys: null-valued existing, alive existing, and new.
        var postBody = handler.SentBodies[1]!;
        var data = JsonDocument.Parse(postBody).RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("DESTROYED_KEY", out var destroyed));
        Assert.Equal(JsonValueKind.Null, destroyed.ValueKind);
        Assert.Equal("alive", data.GetProperty("ALIVE_KEY").GetString());
        Assert.Equal("new-value", data.GetProperty("NEW_KEY").GetString());
    }

    // Description

    [Fact]
    public void Description_IncludesMountAndPath()
    {
        using var provider = new VaultKvProvider(Reference, Token);
        Assert.Equal("vault:secret/myapp/production", provider.Description);
    }

    // Helpers

    private static string VaultReadBody(Dictionary<string, string> data)
    {
        var payload = new { data = new { data } };
        return JsonSerializer.Serialize(payload);
    }

    private static string NullableVaultReadBody(Dictionary<string, string?> data)
    {
        var payload = new { data = new { data } };
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// An <see cref="HttpMessageHandler"/> that serves pre-queued responses in order,
    /// capturing the outgoing request URIs and bodies for assertion.
    /// </summary>
    private sealed class QueuedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _queue = new();

        public List<HttpRequestMessage> SentRequests { get; } = [];
        public List<string?> SentBodies { get; } = [];

        public void Enqueue(HttpStatusCode status, string body) => _queue.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SentRequests.Add(request);
            SentBodies.Add(request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));

            var (status, body) = _queue.Dequeue();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
