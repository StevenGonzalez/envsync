namespace EnvSync.Core.Model;

internal static class EnvironmentKeyValidator
{
    public const string RuleDescription =
        "Environment variable names must start with an ASCII letter or underscore and contain only ASCII letters, digits, and underscores.";

    public static bool IsValid(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (!IsAsciiLetter(key[0]) && key[0] != '_')
        {
            return false;
        }

        for (var index = 1; index < key.Length; index++)
        {
            var character = key[index];
            if (!IsAsciiLetter(character) && !IsAsciiDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    public static void ThrowIfInvalid(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!IsValid(key))
        {
            throw new ArgumentException(RuleDescription, nameof(key));
        }
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsAsciiDigit(char character) =>
        character is >= '0' and <= '9';
}
