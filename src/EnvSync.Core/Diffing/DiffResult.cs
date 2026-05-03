namespace EnvSync.Core.Diffing;

public sealed class DiffResult
{
    public DiffResult(IReadOnlyList<DiffEntry> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<DiffEntry> Entries { get; }

    public bool HasChanges => Entries.Any(static entry => entry.Status != DiffStatus.Match);
}