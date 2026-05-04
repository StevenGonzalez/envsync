using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using EnvSync.Core.Model;
using EnvSync.Core.Providers.AwsSsm;
using NSubstitute;

namespace EnvSync.Core.Tests.Providers;

public sealed class AwsSsmProviderTests
{
    private const string Prefix = "/myapp/prod";

    // ReadAsync

    [Fact]
    public async Task ReadAsync_ReturnsStringParameterAsAvailable()
    {
        var client = BuildClient([
            new Parameter { Name = "/myapp/prod/DATABASE_URL", Value = "postgres://localhost/db", Type = ParameterType.String },
        ]);

        var snapshot = await new AwsSsmProvider(new AwsSsmReference(Prefix), client).ReadAsync();

        var value = snapshot.Values["DATABASE_URL"];
        Assert.Equal(ValueAvailability.Available, value.Availability);
        Assert.Equal("postgres://localhost/db", value.Value);
    }

    [Fact]
    public async Task ReadAsync_ReturnsSecureStringParameterAsHidden()
    {
        // SecureString values are returned as encrypted ciphertext when WithDecryption=false.
        // The provider must never expose garbled ciphertext as an apparent plaintext value.
        var client = BuildClient([
            new Parameter { Name = "/myapp/prod/API_SECRET", Value = "CIPHER_GARBAGE", Type = ParameterType.SecureString },
        ]);

        var snapshot = await new AwsSsmProvider(new AwsSsmReference(Prefix), client).ReadAsync();

        var value = snapshot.Values["API_SECRET"];
        Assert.Equal(ValueAvailability.Hidden, value.Availability);
        Assert.Null(value.Value);
    }

    [Fact]
    public async Task ReadAsync_StripsPathPrefixFromParameterName()
    {
        var client = BuildClient([
            new Parameter { Name = "/myapp/prod/NESTED_KEY", Value = "v", Type = ParameterType.String },
        ]);

        var snapshot = await new AwsSsmProvider(new AwsSsmReference(Prefix), client).ReadAsync();

        Assert.True(snapshot.Values.ContainsKey("NESTED_KEY"), "Expected key 'NESTED_KEY' after prefix strip.");
        Assert.False(snapshot.Values.ContainsKey("/myapp/prod/NESTED_KEY"), "Full SSM path must not be exposed as a key.");
    }

    [Fact]
    public async Task ReadAsync_PaginatesUntilNoNextToken()
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();

        client.GetParametersByPathAsync(
            Arg.Is<GetParametersByPathRequest>(r => r.NextToken == null),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GetParametersByPathResponse
            {
                Parameters = [new Parameter { Name = "/myapp/prod/KEY1", Value = "v1", Type = ParameterType.String }],
                NextToken = "page2token",
            }));

        client.GetParametersByPathAsync(
            Arg.Is<GetParametersByPathRequest>(r => r.NextToken == "page2token"),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GetParametersByPathResponse
            {
                Parameters = [new Parameter { Name = "/myapp/prod/KEY2", Value = "v2", Type = ParameterType.String }],
                NextToken = null,
            }));

        var snapshot = await new AwsSsmProvider(new AwsSsmReference(Prefix), client).ReadAsync();

        Assert.Equal(2, snapshot.Values.Count);
        Assert.Equal("v1", snapshot.Values["KEY1"].Value);
        Assert.Equal("v2", snapshot.Values["KEY2"].Value);
    }

    [Fact]
    public async Task ReadAsync_RequestsRecursiveParametersWithoutDecryption()
    {
        var client = BuildClient([]);

        await new AwsSsmProvider(new AwsSsmReference(Prefix), client).ReadAsync();

        await client.Received(1).GetParametersByPathAsync(
            Arg.Is<GetParametersByPathRequest>(request =>
                request.Path == Prefix &&
                request.Recursive == true &&
                request.WithDecryption == false),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_ReturnsEmptySnapshot_WhenNoParametersExist()
    {
        var client = BuildClient([]);

        var snapshot = await new AwsSsmProvider(new AwsSsmReference(Prefix), client).ReadAsync();

        Assert.Empty(snapshot.Values);
    }

    // WriteAsync

    [Fact]
    public async Task WriteAsync_CallsPutParameter_WithCorrectPathAndValue()
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.PutParameterAsync(Arg.Any<PutParameterRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PutParameterResponse()));

        await new AwsSsmProvider(new AwsSsmReference(Prefix), client)
            .WriteAsync([new ResolvedEnvironmentValue("PORT", "8080", false)]);

        await client.Received(1).PutParameterAsync(
            Arg.Is<PutParameterRequest>(r =>
                r.Name == "/myapp/prod/PORT" &&
                r.Value == "8080" &&
                r.Overwrite == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteAsync_NormalizesTrailingSlashInPathPrefix()
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.PutParameterAsync(Arg.Any<PutParameterRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PutParameterResponse()));

        await new AwsSsmProvider(new AwsSsmReference("/myapp/prod/"), client)
            .WriteAsync([new ResolvedEnvironmentValue("PORT", "8080", false)]);

        await client.Received(1).PutParameterAsync(
            Arg.Is<PutParameterRequest>(request => request.Name == "/myapp/prod/PORT"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteAsync_UsesStringType_ForNonSecretValues()
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.PutParameterAsync(Arg.Any<PutParameterRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PutParameterResponse()));

        await new AwsSsmProvider(new AwsSsmReference(Prefix), client)
            .WriteAsync([new ResolvedEnvironmentValue("APP_ENV", "production", false)]);

        await client.Received(1).PutParameterAsync(
            Arg.Is<PutParameterRequest>(r => r.Type == ParameterType.String),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteAsync_UsesSecureStringType_ForSecretValues()
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.PutParameterAsync(Arg.Any<PutParameterRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PutParameterResponse()));

        await new AwsSsmProvider(new AwsSsmReference(Prefix), client)
            .WriteAsync([new ResolvedEnvironmentValue("DB_PASSWORD", "hunter2", true)]);

        await client.Received(1).PutParameterAsync(
            Arg.Is<PutParameterRequest>(r => r.Type == ParameterType.SecureString),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteAsync_ReturnsUpdatedCountMatchingValuesWritten()
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.PutParameterAsync(Arg.Any<PutParameterRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PutParameterResponse()));

        var result = await new AwsSsmProvider(new AwsSsmReference(Prefix), client)
            .WriteAsync([
                new ResolvedEnvironmentValue("K1", "v1", false),
                new ResolvedEnvironmentValue("K2", "v2", false),
            ]);

        Assert.Equal(2, result.UpdatedCount);
    }

    // Description

    [Fact]
    public void Description_UsesPathPrefix()
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        var provider = new AwsSsmProvider(new AwsSsmReference("/my/service"), client);
        Assert.Equal("ssm:/my/service", provider.Description);
    }

    // Edge cases

    [Fact]
    public async Task ReadAsync_Throws_WhenParameterNameDoesNotStartWithPrefix()
    {
        // Simulate SSM returning a parameter whose name doesn't start with the configured prefix:
        // e.g. a cross-account listing bug or unexpected API behaviour.
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.GetParametersByPathAsync(Arg.Any<GetParametersByPathRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GetParametersByPathResponse
            {
                Parameters =
                [
                    new Parameter { Name = "/other/prefix/UNEXPECTED_KEY", Value = "oops", Type = ParameterType.String },
                ],
                NextToken = null,
            }));

        var provider = new AwsSsmProvider(new AwsSsmReference(Prefix), client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ReadAsync());
    }

    // Helpers

    /// <summary>
    /// Builds a substituted SSM client that returns the given parameters on the first
    /// (and only) page.
    /// </summary>
    private static IAmazonSimpleSystemsManagement BuildClient(List<Parameter> parameters)
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.GetParametersByPathAsync(Arg.Any<GetParametersByPathRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GetParametersByPathResponse
            {
                Parameters = parameters,
                NextToken = null,
            }));
        return client;
    }
}
