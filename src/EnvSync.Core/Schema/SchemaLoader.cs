using System.Text.Json;
using System.Text.Json.Serialization;
using EnvSync.Core.Model;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EnvSync.Core.Schema;

/// <summary>
/// Loads and parses EnvSync schema files.
/// </summary>
public sealed class SchemaLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Loads an environment schema from disk.
    /// </summary>
    /// <param name="path">The schema file path.</param>
    /// <returns>The parsed environment schema.</returns>
    public EnvSchema Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Schema file '{path}' was not found.", path);
        }

        var content = File.ReadAllText(path);
        return Parse(content, DetectFormat(path));
    }

    /// <summary>
    /// Parses a schema payload that has already been read into memory.
    /// </summary>
    /// <param name="content">The schema content.</param>
    /// <param name="format">The schema format.</param>
    /// <returns>The parsed environment schema.</returns>
    public EnvSchema Parse(string content, SchemaFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        try
        {
            var rawDefinitions = format switch
            {
                SchemaFormat.Json => ParseJson(content),
                SchemaFormat.Yaml => ParseYaml(content),
                _ => throw new SchemaParseException($"Unsupported schema format '{format}'."),
            };

            return new EnvSchema(rawDefinitions.Select(static pair => CreateDefinition(pair.Key, pair.Value)));
        }
        catch (SchemaParseException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SchemaParseException("Failed to parse schema file.", exception);
        }
    }

    /// <summary>
    /// Infers the schema format from the file extension.
    /// </summary>
    /// <param name="path">The schema file path.</param>
    /// <returns>The detected schema format.</returns>
    public static SchemaFormat DetectFormat(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => SchemaFormat.Json,
            ".yaml" => SchemaFormat.Yaml,
            ".yml" => SchemaFormat.Yaml,
            _ => throw new SchemaParseException("Schema format must be .json, .yaml, or .yml."),
        };
    }

    private static IReadOnlyDictionary<string, RawSchemaDefinition> ParseJson(string content)
    {
        var definitions = JsonSerializer.Deserialize<Dictionary<string, RawSchemaDefinition>>(content, JsonOptions);
        return definitions ?? throw new SchemaParseException("Schema did not contain any variables.");
    }

    private IReadOnlyDictionary<string, RawSchemaDefinition> ParseYaml(string content)
    {
        var definitions = _yamlDeserializer.Deserialize<Dictionary<string, RawSchemaDefinition>>(content);
        return definitions ?? throw new SchemaParseException("Schema did not contain any variables.");
    }

    private static EnvVariableDefinition CreateDefinition(string name, RawSchemaDefinition rawDefinition)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new SchemaParseException("Schema variable names cannot be empty or whitespace.");
        }

        if (!EnvironmentKeyValidator.IsValid(name))
        {
            throw new SchemaParseException(
                $"Schema variable '{name}' is not a valid environment variable name. {EnvironmentKeyValidator.RuleDescription}");
        }

        var type = ParseType(rawDefinition.Type, name);
        var defaultValue = rawDefinition.DefaultValue is null
            ? null
            : SchemaValueNormalizer.Normalize(rawDefinition.DefaultValue, type);

        var allowedValues = (rawDefinition.AllowedValues ?? [])
            .Select(value => SchemaValueNormalizer.Normalize(value, type))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (defaultValue is not null && allowedValues.Length > 0 && !allowedValues.Contains(defaultValue, StringComparer.Ordinal))
        {
            throw new SchemaParseException($"Default value for '{name}' must be present in the allowed list.");
        }

        return new EnvVariableDefinition
        {
            Name = name,
            Type = type,
            Required = rawDefinition.Required ?? false,
            Secret = rawDefinition.Secret ?? false,
            Description = rawDefinition.Description,
            DefaultValue = defaultValue,
            AllowedValues = allowedValues,
        };
    }

    private static EnvValueType ParseType(string? rawType, string name)
    {
        if (string.IsNullOrWhiteSpace(rawType))
        {
            throw new SchemaParseException($"Schema variable '{name}' is missing a type.");
        }

        return rawType.Trim().ToLowerInvariant() switch
        {
            "string" => EnvValueType.String,
            "number" => EnvValueType.Number,
            "boolean" => EnvValueType.Boolean,
            "bool" => EnvValueType.Boolean,
            _ => throw new SchemaParseException($"Schema variable '{name}' uses unsupported type '{rawType}'."),
        };
    }

    private sealed class RawSchemaDefinition
    {
        public string? Type { get; set; }

        public bool? Required { get; set; }

        public bool? Secret { get; set; }

        public string? Description { get; set; }

        [YamlMember(Alias = "default")]
        [JsonPropertyName("default")]
        public object? DefaultValue { get; set; }

        [YamlMember(Alias = "allowed")]
        [JsonPropertyName("allowed")]
        public List<object>? AllowedValues { get; set; }
    }
}
