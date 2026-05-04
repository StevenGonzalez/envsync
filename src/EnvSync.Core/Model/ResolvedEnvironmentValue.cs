namespace EnvSync.Core.Model;

/// <summary>
/// Represents a schema-managed value ready to be written to a provider.
/// </summary>
/// <param name="Key">The environment variable key.</param>
/// <param name="Value">The plaintext value to write.</param>
/// <param name="Secret">Whether the value should be written as a provider secret.</param>
public sealed record ResolvedEnvironmentValue(string Key, string Value, bool Secret);
