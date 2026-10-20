using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class OperationalRegistryTests
{
    [Theory]
    [InlineData("service-health",OperationalReadKind.HealthOverviews,"/admin/serviceAnnouncement/healthOverviews?$select=id,service,status")]
    [InlineData("service-health",OperationalReadKind.HealthIssues,"/admin/serviceAnnouncement/issues?$select=id,title,service,feature,classification,status,impactDescription,startDateTime,endDateTime,lastModifiedDateTime")]
    [InlineData("partner-relationships",OperationalReadKind.PartnerRelationships,"/tenantRelationships/delegatedAdminRelationships?$top=300")]
    public void Registered_reads_have_fixed_public_graph_paths_and_exact_approved_scope(string report,OperationalReadKind kind,string expected)
    {Assert.Equal(expected,OperationalReportRegistry.Path(report,kind));Assert.Equal(report=="service-health"?"ServiceHealth.Read.All":"DelegatedAdminRelationship.Read.All",OperationalReportRegistry.Scope(report));}
    [Theory][InlineData("not-a-guid_but-opaque")][InlineData("72a7ae7e-4887-4e34-9755-2e1e9b26b943-63f017cb-9e0d-4f14-94bd-4871902b3409")]
    public void Relationship_ids_remain_opaque_exact_segments_not_guessed_guids(string id)
    {Assert.Equal("/tenantRelationships/delegatedAdminRelationships/"+id+"/accessAssignments",OperationalReportRegistry.Path("partner-relationships",OperationalReadKind.PartnerAssignments,id));}
    [Theory][InlineData("../other")][InlineData("x%2Fy")][InlineData("x?x=y")][InlineData("x#frag")][InlineData("x\\y")][InlineData(" ")][InlineData(".")][InlineData("..")]
    public void Parent_ids_cannot_change_path_or_query(string id)
    {Assert.Throws<ConfigurationException>(()=>OperationalReportRegistry.Path("partner-relationships",OperationalReadKind.PartnerAssignments,id));}
    [Theory][InlineData("service-health",OperationalReadKind.PartnerRelationships)][InlineData("partner-relationships",OperationalReadKind.HealthIssues)][InlineData("unknown",OperationalReadKind.HealthOverviews)]
    public void Wrong_module_or_unknown_kind_cannot_expand_the_registered_surface(string report,OperationalReadKind kind)
    {Assert.Throws<ConfigurationException>(()=>OperationalReportRegistry.Path(report,kind));}
}
