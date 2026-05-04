namespace EnvSync.Core.Validation;

/// <summary>
/// Defines the severity levels for schema validation issues.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// The issue should be reported, but does not fail validation.
    /// </summary>
    Warning,

    /// <summary>
    /// The issue fails validation.
    /// </summary>
    Error,
}
