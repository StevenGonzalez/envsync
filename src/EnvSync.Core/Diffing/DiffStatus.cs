namespace EnvSync.Core.Diffing;

/// <summary>
/// Represents the comparison state for one environment variable key.
/// </summary>
public enum DiffStatus
{
    /// <summary>
    /// The value is present and equal on both sides.
    /// </summary>
    Match,

    /// <summary>
    /// The key exists on both sides, but the visible values differ.
    /// </summary>
    ValueMismatch,

    /// <summary>
    /// The schema-managed key is missing from the left side.
    /// </summary>
    MissingFromLeft,

    /// <summary>
    /// The schema-managed key is missing from the right side.
    /// </summary>
    MissingFromRight,

    /// <summary>
    /// The key cannot be compared because at least one side hides the value.
    /// </summary>
    Unknown,

    /// <summary>
    /// The non-schema key exists only on the left side.
    /// </summary>
    UnexpectedLeftOnly,

    /// <summary>
    /// The non-schema key exists only on the right side.
    /// </summary>
    UnexpectedRightOnly,
}
