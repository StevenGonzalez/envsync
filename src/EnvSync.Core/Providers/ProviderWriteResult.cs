namespace EnvSync.Core.Providers;

public sealed class ProviderWriteResult
{
    public ProviderWriteResult(int updatedCount, IReadOnlyList<string>? warnings = null)
    {
        UpdatedCount = updatedCount;
        Warnings = warnings ?? [];
    }

    public int UpdatedCount { get; }

    public IReadOnlyList<string> Warnings { get; }
}