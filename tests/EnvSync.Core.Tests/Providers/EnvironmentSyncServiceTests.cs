using EnvSync.Core.Model;
using EnvSync.Core.Providers;
using EnvSync.Core.Sync;
using EnvSync.Core.Validation;

namespace EnvSync.Core.Tests.Providers;

public sealed class EnvironmentSyncServiceTests
{
    [Fact]
    public async Task SyncAsync_UsesDefaultsWhenSourceValueIsMissing()
    {
        var schema = new EnvSchema([
            new EnvVariableDefinition { Name = "PORT", Type = EnvValueType.Number, DefaultValue = "3000" },
        ]);

        var source = new InMemoryProvider(new EnvironmentSnapshot("source", []));
        var target = new InMemoryProvider(new EnvironmentSnapshot("target", []));
        var service = new EnvironmentSyncService(new SchemaValidator());

        var result = await service.SyncAsync(schema, source, target);

        Assert.Equal(1, result.WrittenCount);
        Assert.Single(target.WrittenValues);
        Assert.Equal("3000", target.WrittenValues[0].Value);
    }

    [Fact]
    public async Task SyncAsync_SkipsHiddenValuesAndReturnsWarning()
    {
        var schema = new EnvSchema([
            new EnvVariableDefinition { Name = "DATABASE_URL", Type = EnvValueType.String, Required = true, Secret = true },
        ]);

        var source = new InMemoryProvider(new EnvironmentSnapshot("source", [EnvironmentValue.Hidden("DATABASE_URL")]));
        var target = new InMemoryProvider(new EnvironmentSnapshot("target", []));
        var service = new EnvironmentSyncService(new SchemaValidator());

        var result = await service.SyncAsync(schema, source, target);

        Assert.Equal(0, result.WrittenCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("Skipped 'DATABASE_URL'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SyncAsync_DoesNotWriteValuesThatAlreadyMatchTarget()
    {
        var schema = new EnvSchema([
            new EnvVariableDefinition { Name = "APP_ENV", Type = EnvValueType.String, Required = true },
        ]);

        var source = new InMemoryProvider(new EnvironmentSnapshot("source", [EnvironmentValue.Available("APP_ENV", "prod")]));
        var target = new InMemoryProvider(new EnvironmentSnapshot("target", [EnvironmentValue.Available("APP_ENV", "prod")]));
        var service = new EnvironmentSyncService(new SchemaValidator());

        var result = await service.SyncAsync(schema, source, target);

        Assert.Equal(0, result.WrittenCount);
        Assert.Equal(0, target.WriteCalls);
        Assert.Empty(target.WrittenValues);
    }

    private sealed class InMemoryProvider : IEnvironmentProvider
    {
        private readonly EnvironmentSnapshot _snapshot;

        public InMemoryProvider(EnvironmentSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public List<ResolvedEnvironmentValue> WrittenValues { get; } = [];

        public int WriteCalls { get; private set; }

        public string Description => _snapshot.SourceName;

        public Task<EnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_snapshot);

        public Task<ProviderWriteResult> WriteAsync(IReadOnlyCollection<ResolvedEnvironmentValue> values, CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            WrittenValues.AddRange(values);
            return Task.FromResult(new ProviderWriteResult(values.Count));
        }
    }
}
