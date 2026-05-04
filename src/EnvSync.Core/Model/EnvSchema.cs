using System.Collections.ObjectModel;

namespace EnvSync.Core.Model;

/// <summary>
/// Represents the declared environment variable schema.
/// </summary>
public sealed class EnvSchema
{
    private readonly IReadOnlyList<EnvVariableDefinition> _definitions;
    private readonly IReadOnlyDictionary<string, EnvVariableDefinition> _definitionMap;

    /// <summary>
    /// Creates a schema from ordered variable definitions.
    /// </summary>
    /// <param name="definitions">The schema variable definitions.</param>
    public EnvSchema(IEnumerable<EnvVariableDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var orderedDefinitions = definitions.ToList();
        if (orderedDefinitions.Count == 0)
        {
            throw new ArgumentException("Schema must contain at least one variable definition.", nameof(definitions));
        }

        var duplicates = orderedDefinitions
            .GroupBy(static definition => definition.Name, StringComparer.Ordinal)
            .Where(static grouping => grouping.Count() > 1)
            .Select(static grouping => grouping.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new ArgumentException($"Schema contains duplicate keys: {string.Join(", ", duplicates)}", nameof(definitions));
        }

        _definitions = new ReadOnlyCollection<EnvVariableDefinition>(orderedDefinitions);
        _definitionMap = new ReadOnlyDictionary<string, EnvVariableDefinition>(
            orderedDefinitions.ToDictionary(static definition => definition.Name, StringComparer.Ordinal));
    }

    /// <summary>
    /// Gets the schema definitions in declaration order.
    /// </summary>
    public IReadOnlyList<EnvVariableDefinition> Definitions => _definitions;

    /// <summary>
    /// Gets schema definitions keyed by environment variable name.
    /// </summary>
    public IReadOnlyDictionary<string, EnvVariableDefinition> DefinitionMap => _definitionMap;

    /// <summary>
    /// Attempts to get a schema definition by environment variable key.
    /// </summary>
    /// <param name="key">The environment variable key.</param>
    /// <param name="definition">The definition when found.</param>
    /// <returns><see langword="true"/> when the definition exists; otherwise <see langword="false"/>.</returns>
    public bool TryGetDefinition(string key, out EnvVariableDefinition definition) => _definitionMap.TryGetValue(key, out definition!);
}
