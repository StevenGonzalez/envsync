namespace EnvSync.Core.Sync;

public sealed class EnvironmentSyncException : Exception
{
    public EnvironmentSyncException(string message)
        : base(message)
    {
    }
}