namespace EnvSync.Core.Validation;

/// <summary>
/// Contains validation issues and the overall validation status.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// Creates a validation result.
    /// </summary>
    /// <param name="issues">The validation issues.</param>
    public ValidationResult(IReadOnlyList<ValidationIssue> issues)
    {
        Issues = issues;
    }

    /// <summary>
    /// Gets all validation issues.
    /// </summary>
    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>
    /// Gets a value indicating whether validation has no errors.
    /// </summary>
    public bool IsSuccess => Issues.All(static issue => issue.Severity != ValidationSeverity.Error);
}
