namespace EnvSync.Core.Providers.AzureAppService;

/// <summary>
/// Identifies an Azure App Service app or deployment slot.
/// </summary>
public sealed record AzureAppServiceReference
{
    /// <summary>
    /// Creates an Azure App Service reference.
    /// </summary>
    /// <param name="subscriptionId">The Azure subscription ID.</param>
    /// <param name="resourceGroupName">The resource group name.</param>
    /// <param name="appName">The App Service app name.</param>
    /// <param name="slotName">The optional deployment slot name.</param>
    public AzureAppServiceReference(
        string subscriptionId,
        string resourceGroupName,
        string appName,
        string? slotName = null)
    {
        if (!Guid.TryParse(subscriptionId, out _))
        {
            throw new ArgumentException("Azure App Service subscription ID must be a GUID.", nameof(subscriptionId));
        }

        if (string.IsNullOrWhiteSpace(resourceGroupName) || resourceGroupName.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("Azure App Service resource group name must be a non-empty path segment.", nameof(resourceGroupName));
        }

        if (string.IsNullOrWhiteSpace(appName) || appName.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("Azure App Service app name must be a non-empty path segment.", nameof(appName));
        }

        if (slotName is not null && (string.IsNullOrWhiteSpace(slotName) || slotName.Contains('/', StringComparison.Ordinal)))
        {
            throw new ArgumentException("Azure App Service slot name must be a non-empty path segment.", nameof(slotName));
        }

        SubscriptionId = subscriptionId;
        ResourceGroupName = resourceGroupName;
        AppName = appName;
        SlotName = slotName;
    }

    /// <summary>
    /// Gets the Azure subscription ID.
    /// </summary>
    public string SubscriptionId { get; init; }

    /// <summary>
    /// Gets the resource group name.
    /// </summary>
    public string ResourceGroupName { get; init; }

    /// <summary>
    /// Gets the App Service app name.
    /// </summary>
    public string AppName { get; init; }

    /// <summary>
    /// Gets the optional deployment slot name.
    /// </summary>
    public string? SlotName { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        SlotName is null
            ? $"{SubscriptionId}/{ResourceGroupName}/{AppName}"
            : $"{SubscriptionId}/{ResourceGroupName}/{AppName}/{SlotName}";
}
