namespace EnvSync.Core.Model;

/// <summary>
/// Describes whether a provider exposed the plaintext value.
/// </summary>
public enum ValueAvailability
{
    /// <summary>
    /// The plaintext value is available.
    /// </summary>
    Available,

    /// <summary>
    /// The provider reports that the value exists but hides its plaintext.
    /// </summary>
    Hidden,
}
