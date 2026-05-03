namespace EnvSync.Core.Providers.AzureDevOps;

public sealed record AzureDevOpsVariableGroupReference
{
    public AzureDevOpsVariableGroupReference(string organization, string project, string groupName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);

        Organization = organization;
        Project = project;
        GroupName = groupName;
    }

    public string Organization { get; init; }

    public string Project { get; init; }

    public string GroupName { get; init; }

    public override string ToString() => $"{Organization}/{Project}/{GroupName}";
}
