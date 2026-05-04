namespace EnvSync.Core.Schema;

/// <summary>
/// Represents an error while parsing an EnvSync schema.
/// </summary>
public sealed class SchemaParseException : Exception
{
    /// <summary>
    /// Creates a schema parse exception with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public SchemaParseException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates a schema parse exception with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public SchemaParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
