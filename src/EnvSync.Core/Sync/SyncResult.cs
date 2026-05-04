namespace EnvSync.Core.Sync;

/// <summary>
/// Reports the outcome of an environment sync operation.
/// </summary>
public sealed class SyncResult
{
    /// <summary>
    /// Creates a sync result.
    /// </summary>
    /// <param name="source">The source provider description.</param>
    /// <param name="target">The target provider description.</param>
    /// <param name="writtenCount">The number of values written.</param>
    /// <param name="warnings">Optional warnings produced during sync.</param>
    public SyncResult(string source, string target, int writtenCount, IReadOnlyList<string>? warnings = null)
    {
        Source = source;
        Target = target;
        WrittenCount = writtenCount;
        Warnings = warnings ?? [];
    }

    /// <summary>
    /// Gets the source provider description.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the target provider description.
    /// </summary>
    public string Target { get; }

    /// <summary>
    /// Gets the number of values written.
    /// </summary>
    public int WrittenCount { get; }

    /// <summary>
    /// Gets warnings produced during sync.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }
}
