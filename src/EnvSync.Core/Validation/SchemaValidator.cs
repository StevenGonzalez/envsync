using EnvSync.Core.Model;
using EnvSync.Core.Schema;

namespace EnvSync.Core.Validation;

public sealed class SchemaValidator
{
    /// <summary>
    /// Validates a provider snapshot against the declared schema.
    /// </summary>
    public ValidationResult Validate(EnvSchema schema, EnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(snapshot);

        var issues = new List<ValidationIssue>();

        foreach (var definition in schema.Definitions)
        {
            if (!snapshot.TryGetValue(definition.Name, out var value))
            {
                if (definition.Required && !definition.HasDefault)
                {
                    issues.Add(new ValidationIssue(
                        definition.Name,
                        "missing_required",
                        $"Required variable '{definition.Name}' is missing.",
                        ValidationSeverity.Error));
                }

                continue;
            }

            if (value.Availability == ValueAvailability.Hidden)
            {
                issues.Add(new ValidationIssue(
                    definition.Name,
                    "value_hidden",
                    $"Variable '{definition.Name}' exists but its value is hidden by the provider and could not be validated.",
                    ValidationSeverity.Warning));
                continue;
            }

            if (!SchemaValueNormalizer.TryNormalizeRuntimeValue(value.Value, definition.Type, out var normalizedValue))
            {
                var typeName = definition.Type switch
                {
                    EnvValueType.String => "string",
                    EnvValueType.Number => "number",
                    EnvValueType.Boolean => "boolean",
                    _ => definition.Type.ToString(),
                };

                issues.Add(new ValidationIssue(
                    definition.Name,
                    "invalid_type",
                    $"Variable '{definition.Name}' value {FormatValueForMessage(value.Value, definition.Secret)} is not a valid {typeName}.",
                    ValidationSeverity.Error));
                continue;
            }

            if (definition.AllowedValues.Count > 0 && normalizedValue is not null && !definition.AllowedValues.Contains(normalizedValue, StringComparer.Ordinal))
            {
                issues.Add(new ValidationIssue(
                    definition.Name,
                    "disallowed_value",
                    $"Variable '{definition.Name}' value {FormatValueForMessage(value.Value, definition.Secret)} is not in the allowed set.",
                    ValidationSeverity.Error));
            }
        }

        return new ValidationResult(issues);
    }

    private static string FormatValueForMessage(string? value, bool isSecret) =>
        isSecret ? "<redacted>" : $"'{value}'";
}
