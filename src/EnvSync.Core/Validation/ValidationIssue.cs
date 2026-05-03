namespace EnvSync.Core.Validation;

public sealed record ValidationIssue(string Key, string Code, string Message, ValidationSeverity Severity);