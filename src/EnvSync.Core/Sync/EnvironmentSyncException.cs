namespace EnvSync.Core.Sync;

/// <summary>
/// Represents a validation or provider error that prevents an environment sync.
/// </summary>
public sealed class EnvironmentSyncException : Exception
{
    /// <summary>
    /// Creates a sync exception with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public EnvironmentSyncException(string message)
        : base(message)
    {
    }
}
