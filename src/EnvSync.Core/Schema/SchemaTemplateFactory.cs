namespace EnvSync.Core.Schema;

/// <summary>
/// Creates sample EnvSync schema files.
/// </summary>
public static class SchemaTemplateFactory
{
    /// <summary>
    /// Creates a starter schema in the requested format.
    /// </summary>
    /// <param name="format">The schema format.</param>
    /// <returns>The sample schema content.</returns>
    public static string CreateSample(SchemaFormat format)
    {
        return format switch
        {
            SchemaFormat.Yaml => """
                APP_ENV:
                  type: string
                  required: true
                  description: Environment name
                  allowed: [dev, staging, prod]

                DATABASE_URL:
                  type: string
                  required: true
                  secret: true
                  description: Connection string for the primary database

                PORT:
                  type: number
                  required: false
                  default: 3000
                  description: Port the HTTP server listens on
                """ + Environment.NewLine,
            SchemaFormat.Json => """
                {
                  "APP_ENV": {
                    "type": "string",
                    "required": true,
                    "description": "Environment name",
                    "allowed": ["dev", "staging", "prod"]
                  },
                  "DATABASE_URL": {
                    "type": "string",
                    "required": true,
                    "secret": true,
                    "description": "Connection string for the primary database"
                  },
                  "PORT": {
                    "type": "number",
                    "required": false,
                    "default": 3000,
                    "description": "Port the HTTP server listens on"
                  }
                }
                """ + Environment.NewLine,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported schema format."),
        };
    }
}
