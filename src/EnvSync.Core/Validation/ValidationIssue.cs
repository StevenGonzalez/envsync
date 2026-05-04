namespace EnvSync.Core.Validation;

/// <summary>
/// Describes one validation issue for a schema key.
/// </summary>
/// <param name="Key">The environment variable key.</param>
/// <param name="Code">The stable machine-readable issue code.</param>
/// <param name="Message">The human-readable issue message.</param>
/// <param name="Severity">The validation severity.</param>
public sealed record ValidationIssue(string Key, string Code, string Message, ValidationSeverity Severity);
