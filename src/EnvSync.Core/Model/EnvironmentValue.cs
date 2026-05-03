namespace EnvSync.Core.Model;

public sealed record EnvironmentValue(string Key, string? Value, ValueAvailability Availability = ValueAvailability.Available)
{
    public static EnvironmentValue Available(string key, string? value) => new(key, value, ValueAvailability.Available);

    public static EnvironmentValue Hidden(string key) => new(key, null, ValueAvailability.Hidden);
}