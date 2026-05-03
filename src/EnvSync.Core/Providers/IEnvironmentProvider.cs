using EnvSync.Core.Model;

namespace EnvSync.Core.Providers;

public interface IEnvironmentProvider
{
    /// <summary>
    /// Gets a human-readable provider description for diagnostics and command output.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Reads all values that the provider can expose.
    /// </summary>
    Task<EnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes resolved values into the provider.
    /// </summary>
    Task<ProviderWriteResult> WriteAsync(IReadOnlyCollection<ResolvedEnvironmentValue> values, CancellationToken cancellationToken = default);
}