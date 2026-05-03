using EnvSync.Core.Model;
using EnvSync.Core.Schema;

namespace EnvSync.Core.Tests;

public sealed class SchemaLoaderTests
{
    private readonly SchemaLoader _loader = new();

    [Fact]
    public void ParseYaml_LoadsDefinitionsInDeclaredOrder()
    {
        const string yaml = """
            APP_ENV:
              type: string
              required: true
              allowed: [dev, staging, prod]
            PORT:
              type: number
              default: 3000
            FEATURE_ENABLED:
              type: boolean
              default: true
            """;

        var schema = _loader.Parse(yaml, SchemaFormat.Yaml);

        Assert.Collection(
            schema.Definitions,
            item =>
            {
                Assert.Equal("APP_ENV", item.Name);
                Assert.Equal(EnvValueType.String, item.Type);
                Assert.Equal(["dev", "staging", "prod"], item.AllowedValues);
            },
            item =>
            {
                Assert.Equal("PORT", item.Name);
                Assert.Equal("3000", item.DefaultValue);
            },
            item =>
            {
                Assert.Equal("FEATURE_ENABLED", item.Name);
                Assert.Equal("true", item.DefaultValue);
            });
    }

    [Fact]
    public void ParseJson_ThrowsWhenDefaultIsOutsideAllowedSet()
    {
        const string json = """
            {
              "APP_ENV": {
                "type": "string",
                "default": "qa",
                "allowed": ["dev", "prod"]
              }
            }
            """;

        var exception = Assert.Throws<SchemaParseException>(() => _loader.Parse(json, SchemaFormat.Json));

        Assert.Contains("Default value", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BAD-NAME")]
    [InlineData("1BAD")]
    [InlineData("BAD.NAME")]
    public void ParseYaml_ThrowsForInvalidVariableNames(string key)
    {
        var yaml = $"""
            {key}:
              type: string
            """;

        var exception = Assert.Throws<SchemaParseException>(() => _loader.Parse(yaml, SchemaFormat.Yaml));

        Assert.Contains("not a valid environment variable name", exception.Message, StringComparison.Ordinal);
    }
}
