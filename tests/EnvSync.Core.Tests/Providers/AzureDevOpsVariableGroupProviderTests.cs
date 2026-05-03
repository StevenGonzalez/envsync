using System.Net;
using System.Text;
using System.Text.Json;
using EnvSync.Core.Model;
using EnvSync.Core.Providers.AzureDevOps;

namespace EnvSync.Core.Tests.Providers;

public sealed class AzureDevOpsVariableGroupProviderTests
{
    [Fact]
    public async Task WriteAsync_UsesAzureDevOpsJsonShapeAndPreservesUnchangedSecretPlaceholders()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
            {
              "count": 1,
              "value": [
                {
                  "id": 42,
                  "name": "production",
                  "type": "Vsts",
                  "variables": {
                    "API_KEY": { "value": null, "isSecret": true },
                    "APP_ENV": { "value": "old", "isSecret": false }
                  }
                }
              ]
            }
            """);
        handler.Enqueue(HttpStatusCode.OK, "{}");

        using var client = new HttpClient(handler);
        using var provider = new AzureDevOpsVariableGroupProvider(
            new AzureDevOpsVariableGroupReference("org", "project", "production"),
            "token",
            client);

        await provider.WriteAsync([new ResolvedEnvironmentValue("APP_ENV", "prod", false)]);

        var putBody = Assert.Single(handler.SentBodies, static body => body is not null)!;
        using var document = JsonDocument.Parse(putBody);
        var variables = document.RootElement.GetProperty("variables");

        var apiKey = variables.GetProperty("API_KEY");
        Assert.True(apiKey.GetProperty("isSecret").GetBoolean());
        Assert.Equal(JsonValueKind.Null, apiKey.GetProperty("value").ValueKind);

        var appEnv = variables.GetProperty("APP_ENV");
        Assert.False(appEnv.GetProperty("isSecret").GetBoolean());
        Assert.Equal("prod", appEnv.GetProperty("value").GetString());
    }

    private sealed class QueuedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _queue = new();

        public List<string?> SentBodies { get; } = [];

        public void Enqueue(HttpStatusCode status, string body) => _queue.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
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
