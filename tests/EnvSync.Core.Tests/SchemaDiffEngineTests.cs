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

    [Fact]
    public void Diff_ReportsMatchWhenBothSidesAreIdentical()
    {
        var left = new EnvironmentSnapshot("local", [
            EnvironmentValue.Available("APP_ENV", "prod"),
            EnvironmentValue.Available("DATABASE_URL", "postgres://prod"),
        ]);

        var right = new EnvironmentSnapshot("staging", [
            EnvironmentValue.Available("APP_ENV", "prod"),
            EnvironmentValue.Available("DATABASE_URL", "postgres://prod"),
        ]);

        var result = _engine.Diff(Schema, left, right);

        Assert.All(result.Entries, entry => Assert.Equal(DiffStatus.Match, entry.Status));
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void Diff_ReportsMissingFromRight_WhenKeyAbsentOnRight()
    {
        var left = new EnvironmentSnapshot("local", [
            EnvironmentValue.Available("APP_ENV", "dev"),
        ]);

        var right = new EnvironmentSnapshot("github", []);

        var result = _engine.Diff(Schema, left, right);

        Assert.Contains(result.Entries, entry => entry.Key == "APP_ENV" && entry.Status == DiffStatus.MissingFromRight);
    }

    [Fact]
    public void Diff_ReportsMissingFromLeft_WhenKeyAbsentOnLeft()
    {
        var left = new EnvironmentSnapshot("local", []);

        var right = new EnvironmentSnapshot("github", [
            EnvironmentValue.Available("APP_ENV", "prod"),
        ]);

        var result = _engine.Diff(Schema, left, right);

        Assert.Contains(result.Entries, entry => entry.Key == "APP_ENV" && entry.Status == DiffStatus.MissingFromLeft);
    }

    [Fact]
    public void Diff_ReportsUnexpectedRightOnly_ForExtraKeyOnRight()
    {
        var left = new EnvironmentSnapshot("local", []);
        var right = new EnvironmentSnapshot("github", [EnvironmentValue.Available("EXTRA", "value")]);

        var result = _engine.Diff(Schema, left, right);

        Assert.Contains(result.Entries, entry => entry.Key == "EXTRA" && entry.Status == DiffStatus.UnexpectedRightOnly);
    }

    [Fact]
    public void Diff_OutputsEntriesInSchemaOrderFirst()
    {
        var left = new EnvironmentSnapshot("local", [
            EnvironmentValue.Available("APP_ENV", "dev"),
            EnvironmentValue.Available("DATABASE_URL", "db"),
            EnvironmentValue.Available("EXTRA", "1"),
        ]);

        var right = new EnvironmentSnapshot("github", [
            EnvironmentValue.Available("APP_ENV", "dev"),
            EnvironmentValue.Available("DATABASE_URL", "db"),
        ]);

        var result = _engine.Diff(Schema, left, right);

        // Schema-declared keys come first, then extras.
        Assert.Equal("APP_ENV", result.Entries[0].Key);
        Assert.Equal("DATABASE_URL", result.Entries[1].Key);
        Assert.Equal("EXTRA", result.Entries[2].Key);
    }
}
