namespace BuildStandard.Core.Models;
/// <summary>Ephemeral fixed read purposes, deliberately selected; never persisted into a standard or tenant profile.</summary>
[Flags]
public enum OptionalReportAccess { None = 0, UsageReports = 1, ServiceHealth = 2, PartnerRelationships = 4, CollaborationSites = 8, CollaborationTeams = 16, SecurityIncidents = 32 }
public static class OptionalReportAccessPolicy
{
    public const string UsageScope = "Reports.Read.All";
    public const string IncidentScope = "SecurityIncident.Read.All";
    public const string HealthScope = "ServiceHealth.Read.All";
    public const string PartnerScope = "DelegatedAdminRelationship.Read.All";
    public const string SiteScope = "Sites.Read.All";
    public const string TeamMemberScope = "TeamMember.Read.All";
    public static IReadOnlyList<string> PreservedOptionalScopes { get; } = Array.AsReadOnly(new[] { UsageScope, HealthScope, PartnerScope, SiteScope, TeamMemberScope, IncidentScope });
    private const OptionalReportAccess Known = OptionalReportAccess.UsageReports | OptionalReportAccess.ServiceHealth | OptionalReportAccess.PartnerRelationships | OptionalReportAccess.CollaborationSites | OptionalReportAccess.CollaborationTeams | OptionalReportAccess.SecurityIncidents;
    public static void Validate(OptionalReportAccess access)
    { if ((access & ~Known) != 0) throw new ConfigurationException("Choose only supported fixed optional report-access purposes."); }
    public static IEnumerable<string> Scopes(OptionalReportAccess access)
    {
        Validate(access);
        if ((access & OptionalReportAccess.UsageReports) != 0) yield return UsageScope;
        if ((access & OptionalReportAccess.SecurityIncidents) != 0) yield return IncidentScope;
        if ((access & OptionalReportAccess.ServiceHealth) != 0) yield return HealthScope;
        if ((access & OptionalReportAccess.PartnerRelationships) != 0) yield return PartnerScope;
        if ((access & OptionalReportAccess.CollaborationSites) != 0) yield return SiteScope;
        if ((access & OptionalReportAccess.CollaborationTeams) != 0) yield return TeamMemberScope;
    }
    public static bool IsSatisfied(OptionalReportAccess access, IEnumerable<string> returnedScopes)
    {
        Validate(access);
        if (access == OptionalReportAccess.None) return true;
        var returned = returnedScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Scopes(access).All(scope => returned.Contains(scope) || returned.Contains("https://graph.microsoft.com/" + scope));
    }
}
