using EnvSync.Core.Model;
using EnvSync.Core.Validation;

namespace EnvSync.Core.Tests;

public sealed class SchemaValidatorTests
{
    private static readonly EnvSchema Schema = new([
        new EnvVariableDefinition { Name = "APP_ENV", Type = EnvValueType.String, Required = true, AllowedValues = ["dev", "prod"] },
        new EnvVariableDefinition { Name = "PORT", Type = EnvValueType.Number, DefaultValue = "3000" },
        new EnvVariableDefinition { Name = "DATABASE_URL", Type = EnvValueType.String, Required = true, Secret = true },
    ]);

    private readonly SchemaValidator _validator = new();

    [Fact]
    public void Validate_ReturnsErrorsForMissingAndInvalidValues()
    {
        var snapshot = new EnvironmentSnapshot("local", [
            EnvironmentValue.Available("APP_ENV", "qa"),
            EnvironmentValue.Available("PORT", "abc")
        ]);

        var result = _validator.Validate(Schema, snapshot);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Key == "APP_ENV" && issue.Code == "disallowed_value");
        Assert.Contains(result.Issues, issue => issue.Key == "PORT" && issue.Code == "invalid_type");
        Assert.Contains(result.Issues, issue => issue.Key == "DATABASE_URL" && issue.Code == "missing_required");
    }

    [Fact]
    public void Validate_ReturnsWarningForHiddenValues()
    {
        var snapshot = new EnvironmentSnapshot("github", [
            EnvironmentValue.Available("APP_ENV", "dev"),
            EnvironmentValue.Hidden("DATABASE_URL")
        ]);

        var result = _validator.Validate(Schema, snapshot);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Key == "DATABASE_URL" && issue.Code == "value_hidden" && issue.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Validate_RedactsSecretValuesInErrors()
    {
        var schema = new EnvSchema([
            new EnvVariableDefinition
            {
                Name = "API_KEY",
                Type = EnvValueType.String,
                Secret = true,
                AllowedValues = ["expected"],
            },
        ]);
        var snapshot = new EnvironmentSnapshot("local", [
            EnvironmentValue.Available("API_KEY", "super-secret-value"),
        ]);

        var result = _validator.Validate(schema, snapshot);

        var issue = Assert.Single(result.Issues);
        Assert.Contains("<redacted>", issue.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", issue.Message, StringComparison.Ordinal);
    }
}
