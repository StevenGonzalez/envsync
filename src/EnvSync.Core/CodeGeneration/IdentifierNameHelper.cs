using System.Text;

namespace EnvSync.Core.CodeGeneration;

internal static class IdentifierNameHelper
{
    public static string ToPascalCase(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var parts = value.Split(['_', '-', '.', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var builder = new StringBuilder();

        foreach (var part in parts)
        {
            builder.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                builder.Append(part[1..].ToLowerInvariant());
            }
        }

        if (builder.Length == 0 || !char.IsLetter(builder[0]))
        {
            builder.Insert(0, 'V');
        }

        return builder.ToString();
    }

    public static string ToCamelCase(string value)
    {
        var pascalCase = ToPascalCase(value);
        return char.ToLowerInvariant(pascalCase[0]) + pascalCase[1..];
    }

    public static bool IsValidIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!IsAsciiLetter(value[0]) && value[0] != '_')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (!IsAsciiLetter(character) && !IsAsciiDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsAsciiDigit(char character) =>
        character is >= '0' and <= '9';
}
