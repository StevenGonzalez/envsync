using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EnvSync.Core.Model;
using EnvSync.Core.Providers.GitHub;
using Sodium;

namespace EnvSync.Core.Tests.Providers;

public sealed class GitHubActionsProviderTests
{
    private static readonly GitHubRepositoryReference Repository = new("owner", "repo");
    private const string Token = "ghp_test-token";

    // ReadAsync

    [Fact]
    public async Task ReadAsync_ReturnsVariablesAndHiddenSecretsWithoutOverwritingVariable()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VariablesBody([
            ("APP_ENV", "production"),
            ("API_KEY", "visible-variable"),
        ]));
        handler.Enqueue(HttpStatusCode.OK, SecretsBody(["API_KEY", "DB_PASSWORD"]));

        using var provider = BuildProvider(handler);

        var snapshot = await provider.ReadAsync();

        Assert.Equal("github:owner/repo", snapshot.SourceName);
        Assert.Equal(3, snapshot.Values.Count);
        Assert.Equal(ValueAvailability.Available, snapshot.Values["APP_ENV"].Availability);
        Assert.Equal("production", snapshot.Values["APP_ENV"].Value);
        Assert.Equal(ValueAvailability.Available, snapshot.Values["API_KEY"].Availability);
        Assert.Equal("visible-variable", snapshot.Values["API_KEY"].Value);
        Assert.Equal(ValueAvailability.Hidden, snapshot.Values["DB_PASSWORD"].Availability);
        Assert.Null(snapshot.Values["DB_PASSWORD"].Value);
    }

    [Fact]
    public async Task ReadAsync_PaginatesVariablesUntilShortPage()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VariablesBody(
            Enumerable.Range(1, 100).Select(static index => ($"KEY_{index}", (string?)$"value-{index}"))));
        handler.Enqueue(HttpStatusCode.OK, VariablesBody([("FINAL_KEY", "final-value")]));
        handler.Enqueue(HttpStatusCode.OK, SecretsBody([]));

        using var provider = BuildProvider(handler);

        var snapshot = await provider.ReadAsync();

        Assert.Equal(101, snapshot.Values.Count);
        Assert.Equal("value-1", snapshot.Values["KEY_1"].Value);
        Assert.Equal("final-value", snapshot.Values["FINAL_KEY"].Value);
        Assert.Collection(
            handler.SentRequests.Where(static request => request.RequestUri.AbsolutePath.EndsWith("/actions/variables", StringComparison.Ordinal)),
            request => Assert.Equal("per_page=100&page=1", request.RequestUri.Query.TrimStart('?')),
            request => Assert.Equal("per_page=100&page=2", request.RequestUri.Query.TrimStart('?')));
    }

    [Fact]
    public async Task ReadAsync_PaginatesSecretsUntilShortPage()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VariablesBody([]));
        handler.Enqueue(HttpStatusCode.OK, SecretsBody(Enumerable.Range(1, 100).Select(static index => $"SECRET_{index}")));
        handler.Enqueue(HttpStatusCode.OK, SecretsBody(["FINAL_SECRET"]));

        using var provider = BuildProvider(handler);

        var snapshot = await provider.ReadAsync();

        Assert.Equal(101, snapshot.Values.Count);
        Assert.Equal(ValueAvailability.Hidden, snapshot.Values["SECRET_1"].Availability);
        Assert.Equal(ValueAvailability.Hidden, snapshot.Values["FINAL_SECRET"].Availability);
        Assert.Collection(
            handler.SentRequests.Where(static request => request.RequestUri.AbsolutePath.EndsWith("/actions/secrets", StringComparison.Ordinal)),
            request => Assert.Equal("per_page=100&page=1", request.RequestUri.Query.TrimStart('?')),
            request => Assert.Equal("per_page=100&page=2", request.RequestUri.Query.TrimStart('?')));
    }

    [Fact]
    public async Task ReadAsync_SendsGitHubHeadersAndTargetsRepositoryEndpoint()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VariablesBody([]));
        handler.Enqueue(HttpStatusCode.OK, SecretsBody([]));

        using var provider = BuildProvider(handler);

        await provider.ReadAsync();

        var request = handler.SentRequests[0];
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/repos/owner/repo/actions/variables", request.RequestUri.AbsolutePath);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal(Token, request.Authorization?.Parameter);
        Assert.Contains("application/vnd.github+json", request.Accept);
        Assert.Contains("EnvSync/1.0", request.UserAgent);
        Assert.True(request.Headers.TryGetValue("X-GitHub-Api-Version", out var apiVersions));
        Assert.Contains("2022-11-28", apiVersions);
    }

    // WriteAsync

    [Fact]
    public async Task WriteAsync_FetchesRepositoryPublicKeyOnlyOnceForMultipleSecrets()
    {
        using var keyPair = PublicKeyBox.GenerateKeyPair();
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, PublicKeyBody("kid", keyPair.PublicKey));
        handler.Enqueue(HttpStatusCode.Created, "{}");
        handler.Enqueue(HttpStatusCode.Created, "{}");

        using var provider = BuildProvider(handler);

        var result = await provider.WriteAsync([
            new ResolvedEnvironmentValue("API_KEY", "secret-1", true),
            new ResolvedEnvironmentValue("DB_PASSWORD", "secret-2", true),
        ]);

        Assert.Equal(2, result.UpdatedCount);
        Assert.Single(handler.SentRequests, request => request.RequestUri.AbsolutePath.EndsWith("/actions/secrets/public-key", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WriteAsync_PutsEncryptedSecretWithReturnedKeyId()
    {
        using var keyPair = PublicKeyBox.GenerateKeyPair();
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, PublicKeyBody("kid", keyPair.PublicKey));
        handler.Enqueue(HttpStatusCode.Created, "{}");

        using var provider = BuildProvider(handler);

        await provider.WriteAsync([new ResolvedEnvironmentValue("API_KEY", "super-secret", true)]);

        var putRequest = Assert.Single(handler.SentRequests, static request => request.Method == HttpMethod.Put);
        Assert.Equal("/repos/owner/repo/actions/secrets/API_KEY", putRequest.RequestUri.AbsolutePath);
        using var document = JsonDocument.Parse(putRequest.Body!);
        Assert.Equal("kid", document.RootElement.GetProperty("key_id").GetString());

        var encryptedValue = document.RootElement.GetProperty("encrypted_value").GetString();
        Assert.False(string.IsNullOrWhiteSpace(encryptedValue));
        Assert.NotEqual("super-secret", encryptedValue);
        _ = Convert.FromBase64String(encryptedValue!);
    }

    [Fact]
    public async Task WriteAsync_PatchesExistingVariable()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NoContent, string.Empty);

        using var provider = BuildProvider(handler);

        var result = await provider.WriteAsync([new ResolvedEnvironmentValue("APP_ENV", "production", false)]);

        Assert.Equal(1, result.UpdatedCount);
        var request = Assert.Single(handler.SentRequests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("/repos/owner/repo/actions/variables/APP_ENV", request.RequestUri.AbsolutePath);
        Assert.Equal("application/json", request.ContentType);

        using var document = JsonDocument.Parse(request.Body!);
        Assert.Equal("APP_ENV", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("production", document.RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public async Task WriteAsync_CreatesVariableWhenPatchReturnsNotFound()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NotFound, """{"message":"Not Found"}""");
        handler.Enqueue(HttpStatusCode.Created, "{}");

        using var provider = BuildProvider(handler);

        await provider.WriteAsync([new ResolvedEnvironmentValue("APP_ENV", "production", false)]);

        Assert.Collection(
            handler.SentRequests,
            request =>
            {
                Assert.Equal(HttpMethod.Patch, request.Method);
                Assert.Equal("/repos/owner/repo/actions/variables/APP_ENV", request.RequestUri.AbsolutePath);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/repos/owner/repo/actions/variables", request.RequestUri.AbsolutePath);
                using var document = JsonDocument.Parse(request.Body!);
                Assert.Equal("APP_ENV", document.RootElement.GetProperty("name").GetString());
                Assert.Equal("production", document.RootElement.GetProperty("value").GetString());
            });
    }

    [Fact]
    public async Task WriteAsync_ThrowsForInvalidEnvironmentKeyBeforeCallingGitHub()
    {
        var handler = new QueuedHttpMessageHandler();
        using var provider = BuildProvider(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.WriteAsync([new ResolvedEnvironmentValue("bad-key", "value", false)]));

        Assert.Empty(handler.SentRequests);
    }

    // Helpers

    private static GitHubActionsProvider BuildProvider(QueuedHttpMessageHandler handler) =>
        new(Repository, Token, new HttpClient(handler));

    private static string VariablesBody(IEnumerable<(string Name, string? Value)> variables) =>
        JsonSerializer.Serialize(new
        {
            variables = variables.Select(static variable => new
            {
                name = variable.Name,
                value = variable.Value,
            }),
        });

    private static string SecretsBody(IEnumerable<string> secrets) =>
        JsonSerializer.Serialize(new
        {
            secrets = secrets.Select(static secret => new { name = secret }),
        });

    private static string PublicKeyBody(string keyId, byte[] publicKey) =>
        JsonSerializer.Serialize(new
        {
            key_id = keyId,
            key = Convert.ToBase64String(publicKey),
        });

    private sealed record SentRequest(
        HttpMethod Method,
        Uri RequestUri,
        AuthenticationHeaderValue? Authorization,
        string Accept,
        string UserAgent,
        IReadOnlyDictionary<string, string[]> Headers,
        string? ContentType,
        string? Body);

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
                : await request.Content.ReadAsStringAsync(cancellationToken);

            SentRequests.Add(new SentRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization,
                string.Join(",", request.Headers.Accept.Select(static value => value.MediaType)),
                string.Join(" ", request.Headers.UserAgent.Select(static value => value.ToString())),
                request.Headers.ToDictionary(static header => header.Key, static header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
                request.Content?.Headers.ContentType?.MediaType,
                body));

            var (status, responseBody) = _queue.Dequeue();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
