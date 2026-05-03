namespace EnvSync.Core.Diffing;

public enum DiffStatus
{
    Match,
    ValueMismatch,
    MissingFromLeft,
    MissingFromRight,
    Unknown,
    UnexpectedLeftOnly,
    UnexpectedRightOnly,
}