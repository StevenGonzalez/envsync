namespace EnvSync.Core.Diffing;

public sealed record DiffEntry(string Key, DiffStatus Status, string? LeftValue, string? RightValue, string? Message);