using EnvSync.Core.CodeGeneration;
using EnvSync.Core.Model;

namespace EnvSync.Core.Tests;

public sealed class BindingGeneratorTests
{
    private static readonly EnvSchema Schema = new([
        new EnvVariableDefinition { Name = "APP_ENV", Type = EnvValueType.String, Required = true, AllowedValues = ["dev", "prod"] },
        new EnvVariableDefinition { Name = "PORT", Type = EnvValueType.Number, DefaultValue = "3000" },
        new EnvVariableDefinition { Name = "OPTIONAL_FLAG", Type = EnvValueType.Boolean },
    ]);

    private readonly BindingGenerator _generator = new();

    [Fact]
    public void Generate_TypeScript_UsesTypedProperties()
    {
        var file = _generator.Generate(Schema, BindingLanguage.TypeScript);

        Assert.Equal("envsync.generated.ts", file.FileName);
        Assert.Contains("export interface EnvSyncEnvironment", file.Content, StringComparison.Ordinal);
        Assert.Contains("appEnv: \"dev\" | \"prod\";", file.Content, StringComparison.Ordinal);
        Assert.Contains("port: number;", file.Content, StringComparison.Ordinal);
        Assert.Contains("optionalFlag?: boolean;", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_CSharp_UsesStronglyTypedPropertiesAndDefaults()
    {
        var file = _generator.Generate(Schema, BindingLanguage.CSharp, scope: "EnvSync.Generated");

        Assert.Equal("EnvSyncEnvironment.g.cs", file.FileName);
        Assert.Contains("namespace EnvSync.Generated;", file.Content, StringComparison.Ordinal);
        Assert.Contains("public required string AppEnv", file.Content, StringComparison.Ordinal);
        Assert.Contains("public double Port { get; init; } = 3000;", file.Content, StringComparison.Ordinal);
        Assert.Contains("public bool? OptionalFlag", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_TypeScript_EscapesStringLiterals()
    {
        var schema = new EnvSchema([
            new EnvVariableDefinition { Name = "APP_ENV", Type = EnvValueType.String, Required = true, AllowedValues = ["dev\"local", "prod\\blue"] },
        ]);

        var file = _generator.Generate(schema, BindingLanguage.TypeScript);

        Assert.Contains("appEnv: \"APP_ENV\",", file.Content, StringComparison.Ordinal);
        Assert.Contains("appEnv: \"dev\\\"local\" | \"prod\\\\blue\";", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_CSharp_EscapesDefaultStringLiterals()
    {
        var schema = new EnvSchema([
            new EnvVariableDefinition { Name = "GREETING", Type = EnvValueType.String, DefaultValue = "hello\n\"world\"\\" },
        ]);

        var file = _generator.Generate(schema, BindingLanguage.CSharp);

        Assert.Contains("public string Greeting { get; init; } = \"hello\\n\\\"world\\\"\\\\\";", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ThrowsWhenGeneratedIdentifiersCollide()
    {
        var schema = new EnvSchema([
            new EnvVariableDefinition { Name = "FOO", Type = EnvValueType.String },
            new EnvVariableDefinition { Name = "_FOO", Type = EnvValueType.String },
        ]);

        var exception = Assert.Throws<ArgumentException>(() => _generator.Generate(schema, BindingLanguage.TypeScript));

        Assert.Contains("both generate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ThrowsForInvalidRequestedTypeName()
    {
        var exception = Assert.Throws<ArgumentException>(() => _generator.Generate(Schema, BindingLanguage.CSharp, rootName: "bad-name"));

        Assert.Contains("not a valid identifier", exception.Message, StringComparison.Ordinal);
    }
}
