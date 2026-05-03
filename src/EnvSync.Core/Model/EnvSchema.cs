using System.Collections.ObjectModel;

namespace EnvSync.Core.Model;

public sealed class EnvSchema
{
    private readonly IReadOnlyList<EnvVariableDefinition> _definitions;
    private readonly IReadOnlyDictionary<string, EnvVariableDefinition> _definitionMap;

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

    public IReadOnlyList<EnvVariableDefinition> Definitions => _definitions;

    public IReadOnlyDictionary<string, EnvVariableDefinition> DefinitionMap => _definitionMap;

    public bool TryGetDefinition(string key, out EnvVariableDefinition definition) => _definitionMap.TryGetValue(key, out definition!);
}