using System.Globalization;
using EnvSync.Core.Model;

namespace EnvSync.Core.Schema;

internal static class SchemaValueNormalizer
{
    public static string Normalize(object? rawValue, EnvValueType targetType)
    {
        if (rawValue is null)
        {
            throw new SchemaParseException("Schema values cannot be null.");
        }

        return targetType switch
        {
            EnvValueType.String => rawValue.ToString() ?? string.Empty,
            EnvValueType.Number => NormalizeNumber(rawValue),
            EnvValueType.Boolean => NormalizeBoolean(rawValue),
            _ => throw new SchemaParseException($"Unsupported schema value type '{targetType}'."),
        };
    }

    public static bool TryNormalizeRuntimeValue(string? rawValue, EnvValueType targetType, out string? normalizedValue)
    {
        normalizedValue = null;

        if (rawValue is null)
        {
            return false;
        }

        switch (targetType)
        {
            case EnvValueType.String:
                normalizedValue = rawValue;
                return true;
            case EnvValueType.Number:
                if (double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number))
                {
                    normalizedValue = number.ToString("G", CultureInfo.InvariantCulture);
                    return true;
                }

                return false;
            case EnvValueType.Boolean:
                if (bool.TryParse(rawValue, out var boolean))
                {
                    normalizedValue = boolean ? "true" : "false";
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static string NormalizeNumber(object rawValue)
    {
        if (rawValue is string stringValue)
        {
            if (double.TryParse(stringValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedFromString))
            {
                return parsedFromString.ToString("G", CultureInfo.InvariantCulture);
            }

            throw new SchemaParseException($"'{stringValue}' is not a valid number.");
        }

        if (rawValue is IConvertible convertible)
        {
            try
            {
                var number = convertible.ToDouble(CultureInfo.InvariantCulture);
                return number.ToString("G", CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
            {
                throw new SchemaParseException($"'{rawValue}' is not a valid number.", exception);
            }
        }

        throw new SchemaParseException($"'{rawValue}' is not a valid number.");
    }

    private static string NormalizeBoolean(object rawValue)
    {
        if (rawValue is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        if (rawValue is string stringValue && bool.TryParse(stringValue, out var parsedBoolean))
        {
            return parsedBoolean ? "true" : "false";
        }

        throw new SchemaParseException($"'{rawValue}' is not a valid boolean.");
    }
}