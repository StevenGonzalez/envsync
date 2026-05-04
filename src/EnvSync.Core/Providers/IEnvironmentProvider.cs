using EnvSync.Core.Model;

namespace EnvSync.Core.Providers;

/// <summary>
/// Defines a source or target that can read and write environment values.
/// </summary>
public interface IEnvironmentProvider
{
    /// <summary>
    /// Gets a human-readable provider description for diagnostics and command output.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Reads all values that the provider can expose.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the read operation.</param>
    /// <returns>A snapshot of provider values.</returns>
    Task<EnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes resolved values into the provider.
    /// </summary>
    /// <param name="values">The resolved values to write.</param>
    /// <param name="cancellationToken">A token that cancels the write operation.</param>
    /// <returns>The provider write result.</returns>
    Task<ProviderWriteResult> WriteAsync(IReadOnlyCollection<ResolvedEnvironmentValue> values, CancellationToken cancellationToken = default);
}
