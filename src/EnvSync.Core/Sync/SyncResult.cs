namespace EnvSync.Core.Sync;

public sealed class SyncResult
{
    public SyncResult(string source, string target, int writtenCount, IReadOnlyList<string>? warnings = null)
    {
        Source = source;
        Target = target;
        WrittenCount = writtenCount;
        Warnings = warnings ?? [];
    }

    public string Source { get; }

    public string Target { get; }

    public int WrittenCount { get; }

    public IReadOnlyList<string> Warnings { get; }
}