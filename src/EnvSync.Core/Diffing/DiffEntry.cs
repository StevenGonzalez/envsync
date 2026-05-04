namespace EnvSync.Core.Diffing;

/// <summary>
/// Describes the comparison result for one environment variable key.
/// </summary>
/// <param name="Key">The environment variable key being compared.</param>
/// <param name="Status">The comparison status for the key.</param>
/// <param name="LeftValue">The value observed on the left side, when available.</param>
/// <param name="RightValue">The value observed on the right side, when available.</param>
/// <param name="Message">An optional human-readable explanation for the status.</param>
public sealed record DiffEntry(string Key, DiffStatus Status, string? LeftValue, string? RightValue, string? Message);
