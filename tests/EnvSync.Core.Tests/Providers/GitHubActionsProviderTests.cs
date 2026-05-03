using System.Net;
using System.Text;
using EnvSync.Core.Model;
using EnvSync.Core.Providers.GitHub;
using Sodium;

namespace EnvSync.Core.Tests.Providers;

public sealed class GitHubActionsProviderTests
{
    [Fact]
    public async Task WriteAsync_FetchesRepositoryPublicKeyOnlyOnceForMultipleSecrets()
    {
        using var keyPair = PublicKeyBox.GenerateKeyPair();
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, $$"""
            {
              "key_id": "kid",
              "key": "{{Convert.ToBase64String(keyPair.PublicKey)}}"
            }
            """);
        handler.Enqueue(HttpStatusCode.Created, "{}");
        handler.Enqueue(HttpStatusCode.Created, "{}");

        using var client = new HttpClient(handler);
        using var provider = new GitHubActionsProvider(
            new GitHubRepositoryReference("owner", "repo"),
            "token",
            client);

        await provider.WriteAsync([
            new ResolvedEnvironmentValue("API_KEY", "secret-1", true),
            new ResolvedEnvironmentValue("DB_PASSWORD", "secret-2", true),
        ]);

        Assert.Single(handler.SentRequests, request => request.RequestUri!.AbsolutePath.EndsWith("/actions/secrets/public-key", StringComparison.Ordinal));
    }

    private sealed class QueuedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _queue = new();

        public List<HttpRequestMessage> SentRequests { get; } = [];

        public void Enqueue(HttpStatusCode status, string body) => _queue.Enqueue((status, body));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SentRequests.Add(request);

            var (status, body) = _queue.Dequeue();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
