namespace EnvSync.Core.Schema;

public sealed class SchemaParseException : Exception
{
    public SchemaParseException(string message)
        : base(message)
    {
    }

    public SchemaParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}