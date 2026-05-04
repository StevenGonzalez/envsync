using System.Collections.ObjectModel;

namespace EnvSync.Core.Model;

/// <summary>
/// Represents the environment variables exposed by one provider at a point in time.
/// </summary>
public sealed class EnvironmentSnapshot
{
    private readonly IReadOnlyDictionary<string, EnvironmentValue> _values;

    /// <summary>
    /// Creates a snapshot for the named source.
    /// </summary>
    /// <param name="sourceName">A human-readable source name for diagnostics.</param>
    /// <param name="values">The values exposed by the source.</param>
    public EnvironmentSnapshot(string sourceName, IEnumerable<EnvironmentValue> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(values);

        SourceName = sourceName;
        _values = new ReadOnlyDictionary<string, EnvironmentValue>(
            values.ToDictionary(static value => value.Key, StringComparer.Ordinal));
    }

    /// <summary>
    /// Gets the human-readable source name.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// Gets values keyed by environment variable name.
    /// </summary>
    public IReadOnlyDictionary<string, EnvironmentValue> Values => _values;

    /// <summary>
    /// Attempts to get a value by environment variable key.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">The value when found.</param>
    /// <returns><see langword="true"/> when the key exists; otherwise <see langword="false"/>.</returns>
    public bool TryGetValue(string key, out EnvironmentValue value) => _values.TryGetValue(key, out value!);
}
