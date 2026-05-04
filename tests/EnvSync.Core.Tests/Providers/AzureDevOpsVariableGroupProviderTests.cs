using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EnvSync.Core.Model;
using EnvSync.Core.Providers.AzureDevOps;

namespace EnvSync.Core.Tests.Providers;

public sealed class AzureDevOpsVariableGroupProviderTests
{
    private const string Token = "ado-test-token";

    // ReadAsync

    [Fact]
    public async Task ReadAsync_ReturnsVisibleVariablesAndHiddenSecrets()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VariableGroupListBody(
            ("APP_ENV", "production", IsSecret: false),
            ("API_KEY", null, IsSecret: true)));

        using var provider = BuildProvider(handler);

        var snapshot = await provider.ReadAsync();

        Assert.Equal("azuredevops:org/project/production", snapshot.SourceName);
        Assert.Equal(ValueAvailability.Available, snapshot.Values["APP_ENV"].Availability);
        Assert.Equal("production", snapshot.Values["APP_ENV"].Value);
        Assert.Equal(ValueAvailability.Hidden, snapshot.Values["API_KEY"].Availability);
        Assert.Null(snapshot.Values["API_KEY"].Value);
    }

    [Fact]
    public async Task ReadAsync_SendsBasicAuthAndTargetsNamedGroup()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VariableGroupListBody(("APP_ENV", "production", IsSecret: false)));

        using var provider = BuildProvider(handler, groupName: "release group");

        await provider.ReadAsync();

        var request = Assert.Single(handler.SentRequests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/org/project/_apis/distributedtask/variablegroups", request.RequestUri.AbsolutePath);
        Assert.Contains("groupName=release%20group", request.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("queryOrder=IdDescending", request.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("top=1", request.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("api-version=7.1", request.RequestUri.Query, StringComparison.Ordinal);
        Assert.Equal("Basic", request.Authorization?.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.ASCII.GetBytes($":{Token}")), request.Authorization?.Parameter);
        Assert.Contains("application/json", request.Accept);
        Assert.Contains("EnvSync/1.0", request.UserAgent);
    }

    [Fact]
    public async Task ReadAsync_ThrowsWhenVariableGroupIsMissing()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"count":0,"value":[]}""");

        using var provider = BuildProvider(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ReadAsync());

        Assert.Contains("production", exception.Message, StringComparison.Ordinal);
        Assert.Contains("project", exception.Message, StringComparison.Ordinal);
    }

    // WriteAsync

    [Fact]
    public async Task WriteAsync_UsesAzureDevOpsJsonShapeAndPreservesUnchangedSecretPlaceholders()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VariableGroupListBody(
            ("API_KEY", null, IsSecret: true),
            ("APP_ENV", "old", IsSecret: false)));
        handler.Enqueue(HttpStatusCode.OK, "{}");

        using var provider = BuildProvider(handler);

        await provider.WriteAsync([new ResolvedEnvironmentValue("APP_ENV", "prod", false)]);

        var putRequest = Assert.Single(handler.SentRequests, static request => request.Method == HttpMethod.Put);
        Assert.Equal("/org/project/_apis/distributedtask/variablegroups/42", putRequest.RequestUri.AbsolutePath);
        Assert.Contains("api-version=7.1", putRequest.RequestUri.Query, StringComparison.Ordinal);
        Assert.Equal("application/json", putRequest.ContentType);

        var putBody = putRequest.Body!;
        using var document = JsonDocument.Parse(putBody);
        Assert.Equal(42, document.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("production", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("Vsts", document.RootElement.GetProperty("type").GetString());

        var variables = document.RootElement.GetProperty("variables");

        var apiKey = variables.GetProperty("API_KEY");
        Assert.True(apiKey.GetProperty("isSecret").GetBoolean());
        Assert.Equal(JsonValueKind.Null, apiKey.GetProperty("value").ValueKind);

        var appEnv = variables.GetProperty("APP_ENV");
        Assert.False(appEnv.GetProperty("isSecret").GetBoolean());
        Assert.Equal("prod", appEnv.GetProperty("value").GetString());
    }

    [Fact]
    public async Task WriteAsync_AddsSecretVariableWhenValueIsMarkedSecret()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VariableGroupListBody(("APP_ENV", "production", IsSecret: false)));
        handler.Enqueue(HttpStatusCode.OK, "{}");

        using var provider = BuildProvider(handler);

        var result = await provider.WriteAsync([new ResolvedEnvironmentValue("DB_PASSWORD", "hunter2", true)]);

        Assert.Equal(1, result.UpdatedCount);
        var putRequest = Assert.Single(handler.SentRequests, static request => request.Method == HttpMethod.Put);
        using var document = JsonDocument.Parse(putRequest.Body!);
        var password = document.RootElement.GetProperty("variables").GetProperty("DB_PASSWORD");
        Assert.True(password.GetProperty("isSecret").GetBoolean());
        Assert.Equal("hunter2", password.GetProperty("value").GetString());
    }

    [Fact]
    public async Task WriteAsync_ThrowsForInvalidEnvironmentKeyBeforePuttingGroup()
    {
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, VariableGroupListBody(("APP_ENV", "production", IsSecret: false)));

        using var provider = BuildProvider(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.WriteAsync([new ResolvedEnvironmentValue("bad-key", "value", false)]));

        Assert.Single(handler.SentRequests, static request => request.Method == HttpMethod.Get);
        Assert.DoesNotContain(handler.SentRequests, static request => request.Method == HttpMethod.Put);
    }

    // Helpers

    private static AzureDevOpsVariableGroupProvider BuildProvider(
        QueuedHttpMessageHandler handler,
        string organization = "org",
        string project = "project",
        string groupName = "production") =>
        new(new AzureDevOpsVariableGroupReference(organization, project, groupName), Token, new HttpClient(handler));

    private static string VariableGroupListBody(params (string Key, string? Value, bool IsSecret)[] variables)
    {
        var variableValues = variables.ToDictionary(
            static variable => variable.Key,
            static variable => new
            {
                value = variable.Value,
                isSecret = variable.IsSecret,
            });

        return JsonSerializer.Serialize(new
        {
            count = 1,
            value = new[]
            {
                new
                {
                    id = 42,
                    name = "production",
                    type = "Vsts",
                    variables = variableValues,
                },
            },
        });
    }

    private sealed record SentRequest(
        HttpMethod Method,
        Uri RequestUri,
        AuthenticationHeaderValue? Authorization,
        string Accept,
        string UserAgent,
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
