using EnvSync.Core.Diffing;
using EnvSync.Core.Model;

namespace EnvSync.Core.Tests;

public sealed class SchemaDiffEngineTests
{
    private static readonly EnvSchema Schema = new([
        new EnvVariableDefinition { Name = "APP_ENV", Type = EnvValueType.String, Required = true },
        new EnvVariableDefinition { Name = "DATABASE_URL", Type = EnvValueType.String, Required = true, Secret = true },
    ]);

    private readonly SchemaDiffEngine _engine = new();

    [Fact]
    public void Diff_ReportsValueMismatchAndHiddenValues()
    {
        var left = new EnvironmentSnapshot("local", [
            EnvironmentValue.Available("APP_ENV", "dev"),
            EnvironmentValue.Available("DATABASE_URL", "postgres://local")
        ]);

        var right = new EnvironmentSnapshot("github", [
            EnvironmentValue.Available("APP_ENV", "prod"),
            EnvironmentValue.Hidden("DATABASE_URL")
        ]);

        var result = _engine.Diff(Schema, left, right);

        Assert.Contains(result.Entries, entry => entry.Key == "APP_ENV" && entry.Status == DiffStatus.ValueMismatch);
        Assert.Contains(result.Entries, entry => entry.Key == "DATABASE_URL" && entry.Status == DiffStatus.Unknown);
    }

    [Fact]
    public void Diff_ReportsUnexpectedExtraKeys()
    {
        var left = new EnvironmentSnapshot("local", [EnvironmentValue.Available("EXTRA", "1")]);
        var right = new EnvironmentSnapshot("github", []);

        var result = _engine.Diff(Schema, left, right);

        Assert.Contains(result.Entries, entry => entry.Key == "EXTRA" && entry.Status == DiffStatus.UnexpectedLeftOnly);
    }
}