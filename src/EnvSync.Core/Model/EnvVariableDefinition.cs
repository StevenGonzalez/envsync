namespace EnvSync.Core.Model;

/// <summary>
/// Describes one environment variable declared in a schema.
/// </summary>
public sealed record EnvVariableDefinition
{
    /// <summary>
    /// Gets the environment variable key.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the expected value type.
    /// </summary>
    public required EnvValueType Type { get; init; }

    /// <summary>
    /// Gets a value indicating whether the variable must be present when no default is declared.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// Gets a value indicating whether the value should be written as a provider secret.
    /// </summary>
    public bool Secret { get; init; }

    /// <summary>
    /// Gets the optional human-readable description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the normalized default value, when declared.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Gets the normalized set of allowed values.
    /// </summary>
    public IReadOnlyList<string> AllowedValues { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether a default value is declared.
    /// </summary>
    public bool HasDefault => DefaultValue is not null;

    /// <summary>
    /// Gets a value indicating whether the variable is optional and has no default value.
    /// </summary>
    public bool IsOptionalWithoutDefault => !Required && !HasDefault;
}
