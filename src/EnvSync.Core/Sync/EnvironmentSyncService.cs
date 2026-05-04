using EnvSync.Core.Model;
using EnvSync.Core.Providers;
using EnvSync.Core.Validation;

namespace EnvSync.Core.Sync;

/// <summary>
/// Coordinates validation, resolution, and writes between environment providers.
/// </summary>
public sealed class EnvironmentSyncService
{
    private readonly SchemaValidator _validator;

    /// <summary>
    /// Creates an environment sync service.
    /// </summary>
    /// <param name="validator">The schema validator used before syncing values.</param>
    public EnvironmentSyncService(SchemaValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        _validator = validator;
    }

    /// <summary>
    /// Copies schema-managed values from one provider to another.
    /// </summary>
    /// <param name="schema">The schema that controls which values sync.</param>
    /// <param name="source">The source provider.</param>
    /// <param name="target">The target provider.</param>
    /// <param name="cancellationToken">A token that cancels the sync operation.</param>
    /// <returns>The sync result.</returns>
    public async Task<SyncResult> SyncAsync(EnvSchema schema, IEnvironmentProvider source, IEnvironmentProvider target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var sourceSnapshot = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
        var validationResult = _validator.Validate(schema, sourceSnapshot);
        var errors = validationResult.Issues.Where(static issue => issue.Severity == ValidationSeverity.Error).ToArray();
        if (errors.Length > 0)
        {
            throw new EnvironmentSyncException(string.Join(Environment.NewLine, errors.Select(static error => error.Message)));
        }

        var resolvedValues = new List<ResolvedEnvironmentValue>();
        var warnings = validationResult.Issues
            .Where(static issue => issue.Severity == ValidationSeverity.Warning)
            .Select(static issue => issue.Message)
            .ToList();

        foreach (var definition in schema.Definitions)
        {
            if (sourceSnapshot.TryGetValue(definition.Name, out var sourceValue))
            {
                if (sourceValue.Availability == ValueAvailability.Hidden)
                {
                    warnings.Add($"Skipped '{definition.Name}' because the source provider hides its value.");
                    continue;
                }

                if (sourceValue.Value is null)
                {
                    warnings.Add($"Skipped '{definition.Name}' because the source provider returned a null value.");
                    continue;
                }

                resolvedValues.Add(new ResolvedEnvironmentValue(definition.Name, sourceValue.Value, definition.Secret));
                continue;
            }

            if (definition.HasDefault && definition.DefaultValue is not null)
            {
                resolvedValues.Add(new ResolvedEnvironmentValue(definition.Name, definition.DefaultValue, definition.Secret));
            }
        }

        var targetSnapshot = await target.ReadAsync(cancellationToken).ConfigureAwait(false);
        var valuesToWrite = resolvedValues
            .Where(value => ShouldWriteValue(value, targetSnapshot))
            .ToArray();

        if (valuesToWrite.Length == 0)
        {
            return new SyncResult(source.Description, target.Description, 0, warnings);
        }

        var writeResult = await target.WriteAsync(valuesToWrite, cancellationToken).ConfigureAwait(false);
        warnings.AddRange(writeResult.Warnings);
        return new SyncResult(source.Description, target.Description, writeResult.UpdatedCount, warnings);
    }

    private static bool ShouldWriteValue(ResolvedEnvironmentValue value, EnvironmentSnapshot targetSnapshot)
    {
        if (!targetSnapshot.TryGetValue(value.Key, out var targetValue))
        {
            return true;
        }

        return targetValue.Availability != ValueAvailability.Available ||
               !string.Equals(targetValue.Value, value.Value, StringComparison.Ordinal);
    }
}
