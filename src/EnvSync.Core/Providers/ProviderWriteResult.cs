namespace EnvSync.Core.Providers;

/// <summary>
/// Reports the outcome of a provider write operation.
/// </summary>
public sealed class ProviderWriteResult
{
    /// <summary>
    /// Creates a provider write result.
    /// </summary>
    /// <param name="updatedCount">The number of values updated by the provider.</param>
    /// <param name="warnings">Optional warnings produced during the write.</param>
    public ProviderWriteResult(int updatedCount, IReadOnlyList<string>? warnings = null)
    {
        UpdatedCount = updatedCount;
        Warnings = warnings ?? [];
    }

    /// <summary>
    /// Gets the number of values updated by the provider.
    /// </summary>
    public int UpdatedCount { get; }

    /// <summary>
    /// Gets warnings produced during the write.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }
}
