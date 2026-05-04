namespace EnvSync.Core.Providers.GitHub;

/// <summary>
/// Identifies a GitHub repository.
/// </summary>
public sealed record GitHubRepositoryReference
{
    /// <summary>
    /// Creates a GitHub repository reference.
    /// </summary>
    /// <param name="owner">The repository owner or organization.</param>
    /// <param name="repository">The repository name.</param>
    public GitHubRepositoryReference(string owner, string repository)
    {
        if (!IsValidOwner(owner))
        {
            throw new ArgumentException("GitHub owner names must contain only ASCII letters, digits, hyphens, or underscores, and cannot start or end with a hyphen.", nameof(owner));
        }

        if (!IsValidRepository(repository))
        {
            throw new ArgumentException("GitHub repository names must contain only ASCII letters, digits, dots, hyphens, or underscores.", nameof(repository));
        }

        Owner = owner;
        Repository = repository;
    }

    /// <summary>
    /// Gets the repository owner or organization.
    /// </summary>
    public string Owner { get; init; }

    /// <summary>
    /// Gets the repository name.
    /// </summary>
    public string Repository { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{Owner}/{Repository}";

    private static bool IsValidOwner(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] == '-' || value[^1] == '-')
        {
            return false;
        }

        return value.All(static character => IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }

    private static bool IsValidRepository(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.All(static character => IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    }

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}
