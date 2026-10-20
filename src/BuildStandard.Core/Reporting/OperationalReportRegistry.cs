namespace BuildStandard.Core.Reporting;
/// <summary>Only registered read surfaces. No caller-provided URL or mutation is supported.</summary>
public enum OperationalReadKind { HealthOverviews, HealthIssues, PartnerRelationships, PartnerAssignments }
public static class OperationalReportRegistry
{
    public const int MaximumBytes=16*1024*1024;
    public const int MaximumRows=5000;
    public const int MaximumCalls=100;
    public const int MaximumRelationships=50;
    public const int MaximumRoles=100;
    public static string Scope(string reportId)=>reportId switch
    {
        "service-health"=>"ServiceHealth.Read.All",
        "partner-relationships"=>"DelegatedAdminRelationship.Read.All",
        _=>throw new ConfigurationException("Unknown operational report. Only registered read surfaces are supported.")
    };
    public static string Path(string reportId,OperationalReadKind kind,string? relationshipId=null)
    {
        _=Scope(reportId);
        if(kind!=OperationalReadKind.PartnerAssignments&&relationshipId is not null)
            throw new ConfigurationException("This registered source does not accept a parent identity.");
        return (reportId,kind) switch
        {
            ("service-health",OperationalReadKind.HealthOverviews)=>"/admin/serviceAnnouncement/healthOverviews?$select=id,service,status",
            ("service-health",OperationalReadKind.HealthIssues)=>"/admin/serviceAnnouncement/issues?$select=id,title,service,feature,classification,status,impactDescription,startDateTime,endDateTime,lastModifiedDateTime",
            ("partner-relationships",OperationalReadKind.PartnerRelationships)=>"/tenantRelationships/delegatedAdminRelationships?$top=300",
            ("partner-relationships",OperationalReadKind.PartnerAssignments)=>"/tenantRelationships/delegatedAdminRelationships/"+SafeRelationshipSegment(relationshipId)+"/accessAssignments",
            _=>throw new ConfigurationException("This read is not registered for the selected report.")
        };
    }
    private static string SafeRelationshipSegment(string? id)
    {
        if(string.IsNullOrEmpty(id)||id.Length>512||id is "." or ".."||id.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)||"/\\%?#".Contains(c)))
            throw new ConfigurationException("Relationship identity cannot be admitted as an exact path segment.");
        return Uri.EscapeDataString(id);
    }
}
