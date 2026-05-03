namespace EnvSync.Cli.Commands;

internal sealed class CommandLineUsageException : Exception
{
    public CommandLineUsageException(string message)
        : base(message)
    {
    }
}