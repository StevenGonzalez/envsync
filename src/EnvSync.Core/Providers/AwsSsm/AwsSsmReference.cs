namespace EnvSync.Core.Providers.AwsSsm;

/// <summary>
/// Identifies an AWS Systems Manager Parameter Store path prefix.
/// All parameters whose names begin with <see cref="PathPrefix"/> are treated
/// as a single flat key-value environment.
/// </summary>
public sealed record AwsSsmReference
{
    public AwsSsmReference(string pathPrefix, string? region = null)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix) || !pathPrefix.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("AWS SSM path prefixes must start with '/'.", nameof(pathPrefix));
        }

        PathPrefix = pathPrefix;
        Region = region;
    }

    /// <summary>
    /// The SSM path prefix, e.g. <c>/myapp/production</c>.
    /// Parameters named <c>/myapp/production/DATABASE_URL</c> will surface as <c>DATABASE_URL</c>.
    /// </summary>
    public string PathPrefix { get; init; }

    /// <summary>
    /// Optional AWS region override, e.g. <c>us-east-1</c>.
    /// When <see langword="null"/> the SDK resolves the region from the standard credential chain
    /// (<c>AWS_DEFAULT_REGION</c>, <c>~/.aws/config</c>, EC2 instance metadata, etc.).
    /// </summary>
    public string? Region { get; init; }

    public override string ToString() => PathPrefix;
}
