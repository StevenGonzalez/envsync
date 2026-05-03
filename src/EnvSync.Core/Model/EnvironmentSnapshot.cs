using System.Collections.ObjectModel;

namespace EnvSync.Core.Model;

public sealed class EnvironmentSnapshot
{
    private readonly IReadOnlyDictionary<string, EnvironmentValue> _values;

    public EnvironmentSnapshot(string sourceName, IEnumerable<EnvironmentValue> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(values);

        SourceName = sourceName;
        _values = new ReadOnlyDictionary<string, EnvironmentValue>(
            values.ToDictionary(static value => value.Key, StringComparer.Ordinal));
    }

    public string SourceName { get; }

    public IReadOnlyDictionary<string, EnvironmentValue> Values => _values;

    public bool TryGetValue(string key, out EnvironmentValue value) => _values.TryGetValue(key, out value!);
}