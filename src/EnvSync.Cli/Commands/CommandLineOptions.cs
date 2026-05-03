namespace EnvSync.Cli.Commands;

internal sealed class CommandLineOptions
{
    private readonly Dictionary<string, string?> _options;

    private CommandLineOptions(Dictionary<string, string?> options, List<string> positionals)
    {
        _options = options;
        Positionals = positionals;
    }

    public IReadOnlyList<string> Positionals { get; }

    public IEnumerable<string> OptionNames => _options.Keys;

    public static CommandLineOptions Parse(string[] args)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var current = args[index];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(current);
                continue;
            }

            var rawName = current[2..];
            string name;
            string? inlineValue = null;
            var equalsIndex = rawName.IndexOf('=');
            if (equalsIndex >= 0)
            {
                name = rawName[..equalsIndex];
                inlineValue = rawName[(equalsIndex + 1)..];
            }
            else
            {
                name = rawName;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new CommandLineUsageException("Option names cannot be empty.");
            }

            if (options.ContainsKey(name))
            {
                throw new CommandLineUsageException($"Option '--{name}' was specified more than once.");
            }

            if (inlineValue is not null)
            {
                options[name] = inlineValue;
            }
            else if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[name] = args[index + 1];
                index++;
            }
            else
            {
                options[name] = null;
            }
        }

        return new CommandLineOptions(options, positionals);
    }

    public string GetRequired(string name)
    {
        if (_options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new CommandLineUsageException($"Missing required option '--{name}'.");
    }

    public string? GetOptional(string name, string? defaultValue = null)
    {
        return _options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    public bool HasFlag(string name) => _options.ContainsKey(name) && _options[name] is null;

    public bool HasValue(string name) => _options.TryGetValue(name, out var value) && value is not null;
}
