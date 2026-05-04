namespace EnvSync.Core.Providers.AzureDevOps;

/// <summary>
/// Identifies an Azure DevOps Library variable group.
/// </summary>
public sealed record AzureDevOpsVariableGroupReference
{
    /// <summary>
    /// Creates an Azure DevOps variable group reference.
    /// </summary>
    /// <param name="organization">The Azure DevOps organization name.</param>
    /// <param name="project">The Azure DevOps project name.</param>
    /// <param name="groupName">The variable group name.</param>
    public AzureDevOpsVariableGroupReference(string organization, string project, string groupName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);

        Organization = organization;
        Project = project;
        GroupName = groupName;
    }

    /// <summary>
    /// Gets the Azure DevOps organization name.
    /// </summary>
    public string Organization { get; init; }

    /// <summary>
    /// Gets the Azure DevOps project name.
    /// </summary>
    public string Project { get; init; }

    /// <summary>
    /// Gets the variable group name.
    /// </summary>
    public string GroupName { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{Organization}/{Project}/{GroupName}";
}
