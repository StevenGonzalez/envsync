using EnvSync.Core.Model;

namespace EnvSync.Core.Diffing;

public sealed class SchemaDiffEngine
{
    public DiffResult Diff(EnvSchema schema, EnvironmentSnapshot left, EnvironmentSnapshot right)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var orderedKeys = schema.Definitions.Select(static definition => definition.Name)
            .Concat(left.Values.Keys.Where(key => !schema.DefinitionMap.ContainsKey(key)))
            .Concat(right.Values.Keys.Where(key => !schema.DefinitionMap.ContainsKey(key)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var entries = new List<DiffEntry>(orderedKeys.Length);

        foreach (var key in orderedKeys)
        {
            var hasLeft = left.TryGetValue(key, out var leftValue);
            var hasRight = right.TryGetValue(key, out var rightValue);
            var isSchemaKey = schema.DefinitionMap.ContainsKey(key);

            entries.Add(CreateEntry(key, isSchemaKey, hasLeft ? leftValue : null, hasRight ? rightValue : null));
        }

        return new DiffResult(entries);
    }

    private static DiffEntry CreateEntry(string key, bool isSchemaKey, EnvironmentValue? left, EnvironmentValue? right)
    {
        if (left is null && right is null)
        {
            return new DiffEntry(key, DiffStatus.Match, null, null, null);
        }

        if (!isSchemaKey)
        {
            if (left is not null && right is null)
            {
                return new DiffEntry(key, DiffStatus.UnexpectedLeftOnly, left.Value, null, $"'{key}' exists only on the left side.");
            }

            if (left is null && right is not null)
            {
                return new DiffEntry(key, DiffStatus.UnexpectedRightOnly, null, right.Value, $"'{key}' exists only on the right side.");
            }
        }

        if (left is null)
        {
            return new DiffEntry(key, DiffStatus.MissingFromLeft, null, right?.Value, $"'{key}' is missing from the left side.");
        }

        if (right is null)
        {
            return new DiffEntry(key, DiffStatus.MissingFromRight, left.Value, null, $"'{key}' is missing from the right side.");
        }

        if (left.Availability == ValueAvailability.Hidden || right.Availability == ValueAvailability.Hidden)
        {
            return new DiffEntry(key, DiffStatus.Unknown, left.Value, right.Value, $"'{key}' cannot be compared because one side hides the value.");
        }

        if (string.Equals(left.Value, right.Value, StringComparison.Ordinal))
        {
            return new DiffEntry(key, DiffStatus.Match, left.Value, right.Value, null);
        }

        return new DiffEntry(key, DiffStatus.ValueMismatch, left.Value, right.Value, $"'{key}' differs between environments.");
    }
}