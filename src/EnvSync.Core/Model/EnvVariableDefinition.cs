namespace EnvSync.Core.Model;

public sealed record EnvVariableDefinition
{
    public required string Name { get; init; }

    public required EnvValueType Type { get; init; }

    public bool Required { get; init; }

    public bool Secret { get; init; }

    public string? Description { get; init; }

    public string? DefaultValue { get; init; }

    public IReadOnlyList<string> AllowedValues { get; init; } = [];

    public bool HasDefault => DefaultValue is not null;

    public bool IsOptionalWithoutDefault => !Required && !HasDefault;
}