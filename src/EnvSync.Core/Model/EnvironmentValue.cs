namespace EnvSync.Core.Model;

/// <summary>
/// Represents one environment variable value and whether its plaintext is available.
/// </summary>
/// <param name="Key">The environment variable key.</param>
/// <param name="Value">The plaintext value when available.</param>
/// <param name="Availability">The availability state for the value.</param>
public sealed record EnvironmentValue(string Key, string? Value, ValueAvailability Availability = ValueAvailability.Available)
{
    /// <summary>
    /// Creates an environment value whose plaintext is available.
    /// </summary>
    /// <param name="key">The environment variable key.</param>
    /// <param name="value">The plaintext value.</param>
    /// <returns>The available environment value.</returns>
    public static EnvironmentValue Available(string key, string? value) => new(key, value, ValueAvailability.Available);

    /// <summary>
    /// Creates an environment value whose plaintext is hidden by the provider.
    /// </summary>
    /// <param name="key">The environment variable key.</param>
    /// <returns>The hidden environment value.</returns>
    public static EnvironmentValue Hidden(string key) => new(key, null, ValueAvailability.Hidden);
}
