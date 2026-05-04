namespace EnvSync.Core.Providers.AzureKeyVault;

/// <summary>
/// Identifies an Azure Key Vault instance.
/// </summary>
public sealed record AzureKeyVaultReference
{
    /// <summary>
    /// Creates an Azure Key Vault reference.
    /// </summary>
    /// <param name="vaultName">The short Key Vault name.</param>
    public AzureKeyVaultReference(string vaultName)
    {
        if (!IsValidVaultName(vaultName))
        {
            throw new ArgumentException(
                "Azure Key Vault names must be 3-24 characters, start with a letter, end with a letter or digit, and contain only ASCII letters, digits, and hyphens.",
                nameof(vaultName));
        }

        VaultName = vaultName;
    }

    /// <summary>
    /// The short vault name, e.g. <c>my-vault</c>.
    /// The provider constructs the full URI as <c>https://{VaultName}.vault.azure.net</c>.
    /// </summary>
    public string VaultName { get; init; }

    /// <summary>
    /// Gets the fully-qualified Azure Key Vault URI.
    /// </summary>
    public string VaultUri => $"https://{VaultName}.vault.azure.net";

    /// <inheritdoc />
    public override string ToString() => VaultName;

    private static bool IsValidVaultName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length is < 3 or > 24 ||
            !IsAsciiLetter(value[0]) ||
            !IsAsciiLetterOrDigit(value[^1]))
        {
            return false;
        }

        return value.All(static character => IsAsciiLetterOrDigit(character) || character == '-');
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsAsciiLetterOrDigit(char character) =>
        IsAsciiLetter(character) || character is >= '0' and <= '9';
}
