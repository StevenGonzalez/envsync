namespace EnvSync.Core.Providers.GitHub;

public sealed record GitHubRepositoryReference
{
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

    public string Owner { get; init; }

    public string Repository { get; init; }

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
