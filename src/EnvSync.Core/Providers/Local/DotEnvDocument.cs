using System.Text;
using EnvSync.Core.Model;

namespace EnvSync.Core.Providers.Local;

internal sealed class DotEnvDocument
{
    private readonly List<DotEnvLine> _lines;

    private DotEnvDocument(List<DotEnvLine> lines)
    {
        _lines = lines;
    }

    public static DotEnvDocument Parse(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return new DotEnvDocument([]);
        }

        var lines = new List<DotEnvLine>();
        var rawLines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        foreach (var rawLine in rawLines)
        {
            if (TryParseAssignment(rawLine, out var assignmentLine))
            {
                lines.Add(assignmentLine);
            }
            else
            {
                lines.Add(new RawLine(rawLine));
            }
        }

        return new DotEnvDocument(lines);
    }

    public IReadOnlyCollection<KeyValuePair<string, string>> GetAssignments()
    {
        return _lines
            .OfType<AssignmentLine>()
            .Select(static line => new KeyValuePair<string, string>(line.Key, line.Value))
            .ToArray();
    }

    public void Set(string key, string value)
    {
        EnvironmentKeyValidator.ThrowIfInvalid(key);

        var index = _lines.FindIndex(line => line is AssignmentLine assignmentLine && string.Equals(assignmentLine.Key, key, StringComparison.Ordinal));
        if (index >= 0)
        {
            _lines[index] = new AssignmentLine(key, value);
            return;
        }

        _lines.Add(new AssignmentLine(key, value));
    }

    public string Serialize()
    {
        return string.Join("\n", _lines.Select(static line => line.Serialize())) + "\n";
    }

    private static bool TryParseAssignment(string rawLine, out AssignmentLine assignmentLine)
    {
        assignmentLine = null!;

        var trimmed = rawLine.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return false;
        }

        var line = trimmed.StartsWith("export ", StringComparison.Ordinal)
            ? trimmed[7..]
            : trimmed;

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var key = line[..separatorIndex].Trim();
        if (key.Length == 0)
        {
            return false;
        }

        var rawValue = StripInlineComment(line[(separatorIndex + 1)..].Trim());
        assignmentLine = new AssignmentLine(key, Unescape(rawValue));
        return true;
    }

    // Strips a trailing `# comment` from an unquoted value. Quoted values are returned unchanged
    // because the comment could be part of the string content.
    private static string StripInlineComment(string rawValue)
    {
        if (rawValue.Length == 0)
        {
            return rawValue;
        }

        var first = rawValue[0];
        if (first == '"' || first == '\'')
        {
            return rawValue;
        }

        var commentIndex = rawValue.IndexOf('#');
        return commentIndex > 0 ? rawValue[..commentIndex].TrimEnd() : rawValue;
    }

    private static string Unescape(string rawValue)
    {
        if (rawValue.Length >= 2 && rawValue[0] == '"' && rawValue[^1] == '"')
        {
            var inner = rawValue[1..^1];
            return inner
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        if (rawValue.Length >= 2 && rawValue[0] == '\'' && rawValue[^1] == '\'')
        {
            return rawValue[1..^1];
        }

        return rawValue;
    }

    private static string Escape(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        var requiresQuotes = value.Any(static character => char.IsWhiteSpace(character) || character is '#' or '=' or '"' or '\'' or '\n' or '\r');
        if (!requiresQuotes)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        foreach (var character in value)
        {
            _ = character switch
            {
                '\\' => builder.Append("\\\\"),
                '"' => builder.Append("\\\""),
                '\n' => builder.Append("\\n"),
                '\r' => builder.Append("\\r"),
                _ => builder.Append(character),
            };
        }

        builder.Append('"');
        return builder.ToString();
    }

    private abstract record DotEnvLine
    {
        public abstract string Serialize();
    }

    private sealed record RawLine(string Text) : DotEnvLine
    {
        public override string Serialize() => Text;
    }

    private sealed record AssignmentLine(string Key, string Value) : DotEnvLine
    {
        public override string Serialize() => $"{Key}={Escape(Value)}";
    }
}
