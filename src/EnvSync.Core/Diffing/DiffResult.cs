namespace EnvSync.Core.Diffing;

/// <summary>
/// Contains all entries produced by a schema-aware environment diff.
/// </summary>
public sealed class DiffResult
{
    /// <summary>
    /// Creates a diff result from the supplied entries.
    /// </summary>
    /// <param name="entries">The ordered diff entries.</param>
    public DiffResult(IReadOnlyList<DiffEntry> entries)
    {
        Entries = entries;
    }

    /// <summary>
    /// Gets the ordered diff entries.
    /// </summary>
    public IReadOnlyList<DiffEntry> Entries { get; }

    /// <summary>
    /// Gets a value indicating whether any entry differs between sides.
    /// </summary>
    public bool HasChanges => Entries.Any(static entry => entry.Status != DiffStatus.Match);
}
