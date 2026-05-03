namespace EnvSync.Core.Validation;

public sealed class ValidationResult
{
    public ValidationResult(IReadOnlyList<ValidationIssue> issues)
    {
        Issues = issues;
    }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool IsSuccess => Issues.All(static issue => issue.Severity != ValidationSeverity.Error);
}