namespace EnvSync.Core.Model;

public sealed record ResolvedEnvironmentValue(string Key, string Value, bool Secret);