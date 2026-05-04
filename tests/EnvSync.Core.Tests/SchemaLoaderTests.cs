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
    public void ParseJson_LoadsDefinitionsCorrectly()
    {
        const string json = """
            {
              "APP_ENV": {
                "type": "string",
                "required": true,
                "description": "Deployment environment"
              },
              "PORT": {
                "type": "number",
                "default": 3000
              }
            }
            """;

        var schema = _loader.Parse(json, SchemaFormat.Json);

        Assert.Equal(2, schema.Definitions.Count);
        Assert.Equal("APP_ENV", schema.Definitions[0].Name);
        Assert.True(schema.Definitions[0].Required);
        Assert.Equal("Deployment environment", schema.Definitions[0].Description);
        Assert.Equal("PORT", schema.Definitions[1].Name);
        Assert.Equal("3000", schema.Definitions[1].DefaultValue);
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

    [Fact]
    public void ParseYaml_AcceptsBoolAsAlias()
    {
        const string yaml = """
            DEBUG:
              type: bool
              default: false
            """;

        var schema = _loader.Parse(yaml, SchemaFormat.Yaml);

        Assert.Single(schema.Definitions);
        Assert.Equal(EnvValueType.Boolean, schema.Definitions[0].Type);
        Assert.Equal("false", schema.Definitions[0].DefaultValue);
    }

    [Fact]
    public void ParseYaml_ThrowsWhenTypeIsMissing()
    {
        const string yaml = """
            PORT:
              required: true
            """;

        var exception = Assert.Throws<SchemaParseException>(() => _loader.Parse(yaml, SchemaFormat.Yaml));

        Assert.Contains("missing a type", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseYaml_ThrowsForUnsupportedType()
    {
        const string yaml = """
            PORT:
              type: integer
            """;

        var exception = Assert.Throws<SchemaParseException>(() => _loader.Parse(yaml, SchemaFormat.Yaml));

        Assert.Contains("unsupported type", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void ParseYaml_LoadsDescriptionAndSecretFlags()
    {
        const string yaml = """
            API_KEY:
              type: string
              required: true
              secret: true
              description: Third-party API key
            """;

        var schema = _loader.Parse(yaml, SchemaFormat.Yaml);

        var definition = Assert.Single(schema.Definitions);
        Assert.True(definition.Secret);
        Assert.Equal("Third-party API key", definition.Description);
    }

    [Theory]
    [InlineData("schema.json", SchemaFormat.Json)]
    [InlineData("schema.yaml", SchemaFormat.Yaml)]
    [InlineData("schema.yml", SchemaFormat.Yaml)]
    [InlineData("SCHEMA.YAML", SchemaFormat.Yaml)]
    public void DetectFormat_ReturnsCorrectFormat(string path, SchemaFormat expected)
    {
        Assert.Equal(expected, SchemaLoader.DetectFormat(path));
    }

    [Fact]
    public void DetectFormat_ThrowsForUnknownExtension()
    {
        var exception = Assert.Throws<SchemaParseException>(() => SchemaLoader.DetectFormat("schema.toml"));
        Assert.Contains(".json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        Assert.Throws<FileNotFoundException>(() => _loader.Load("/nonexistent/path/schema.yaml"));
    }
}
