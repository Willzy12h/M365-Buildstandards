namespace BuildStandard.Core.Reporting;
/// <summary>Only the metadata and direct-role read surfaces approved by INT-135.</summary>
public enum CollaborationReadKind { SiteSearch, SiteMetadata, SiteLibraries, GroupMetadata, GroupOwners, TeamMembers }
public static class CollaborationInventoryRegistry
{
    private const string SiteFields = "id,name,displayName,webUrl,siteCollection,createdDateTime,lastModifiedDateTime";
    public static string Scope(CollaborationReadKind kind) => kind switch
    {
        CollaborationReadKind.SiteSearch or CollaborationReadKind.SiteMetadata or CollaborationReadKind.SiteLibraries => "Sites.Read.All",
        CollaborationReadKind.GroupMetadata or CollaborationReadKind.GroupOwners => "Group.Read.All",
        CollaborationReadKind.TeamMembers => "TeamMember.Read.All",
        _ => throw new ConfigurationException("This collaboration source is not registered.")
    };
    public static string Path(CollaborationReadKind kind, string value)
    {
        _ = Scope(kind);
        if (kind == CollaborationReadKind.SiteSearch)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
                throw new ConfigurationException("Use a nonblank search of at most 256 characters without control characters.");
            return "/sites?search=" + Uri.EscapeDataString(value) + "&$select=" + SiteFields;
        }
        if (kind is CollaborationReadKind.SiteMetadata or CollaborationReadKind.SiteLibraries)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 512 || value is "." or ".."
                || value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || "/\\%?#".Contains(c)))
                throw new ConfigurationException("Use an exact returned site identity, never a path or URL.");
            var site = "/sites/" + Uri.EscapeDataString(value);
            return kind == CollaborationReadKind.SiteMetadata ? site + "?$select=" + SiteFields
                : site + "/drives?$select=id,name,driveType,owner,webUrl,createdDateTime,lastModifiedDateTime";
        }
        if (!ReportValues.IsCanonicalGuid(value)) throw new ConfigurationException("Use an exact selected group GUID, never a name or URL.");
        return kind switch
        {
            CollaborationReadKind.GroupMetadata => "/groups/" + value + "?$select=id,displayName,groupTypes,mailEnabled,securityEnabled,onPremisesSyncEnabled,resourceProvisioningOptions",
            CollaborationReadKind.GroupOwners => "/groups/" + value + "/owners?$select=id,displayName",
            CollaborationReadKind.TeamMembers => "/teams/" + value + "/members?$select=id,displayName,roles,userId,tenantId",
            _ => throw new ConfigurationException("This collaboration source is not registered.")
        };
    }
}
