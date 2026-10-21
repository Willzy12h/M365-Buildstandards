using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class DeploymentCoverageReadTests
{
    [Theory]
    [InlineData(CoverageFamily.ConditionalAccess,CoverageReadKind.Object,"/identity/conditionalAccess/policies/","Policy.Read.All","")]
    [InlineData(CoverageFamily.Application,CoverageReadKind.Object,"/deviceAppManagement/mobileApps/","DeviceManagementApps.Read.All","")]
    [InlineData(CoverageFamily.ClassicCompliance,CoverageReadKind.Object,"/deviceManagement/deviceCompliancePolicies/","DeviceManagementConfiguration.Read.All","")]
    [InlineData(CoverageFamily.ClassicConfiguration,CoverageReadKind.Object,"/deviceManagement/deviceConfigurations/","DeviceManagementConfiguration.Read.All","")]
    [InlineData(CoverageFamily.Application,CoverageReadKind.Assignments,"/deviceAppManagement/mobileApps/","DeviceManagementApps.Read.All","/assignments")]
    [InlineData(CoverageFamily.ClassicCompliance,CoverageReadKind.Assignments,"/deviceManagement/deviceCompliancePolicies/","DeviceManagementConfiguration.Read.All","/assignments")]
    [InlineData(CoverageFamily.ClassicConfiguration,CoverageReadKind.Assignments,"/deviceManagement/deviceConfigurations/","DeviceManagementConfiguration.Read.All","/assignments")]
    [InlineData(CoverageFamily.ClassicCompliance,CoverageReadKind.ServiceResults,"/deviceManagement/deviceCompliancePolicies/","DeviceManagementConfiguration.Read.All","/deviceStatuses")]
    public void Only_fixed_registered_reads_are_compiled(CoverageFamily family,CoverageReadKind kind,string root,string scope,string suffix)
    {var r=DeploymentCoverageReads.Prepare(family,TestData.Office,kind);Assert.Equal(root+TestData.Office+suffix,r.Path);Assert.Equal(scope,r.Scope);Assert.Equal(GraphApi.V1,r.Api);Assert.Equal(TestData.Office,r.TargetId);}
    [Theory][InlineData(CoverageFamily.Application)][InlineData(CoverageFamily.ClassicConfiguration)][InlineData(CoverageFamily.ConditionalAccess)]
    public void Unsupported_results_do_not_silently_use_deprecated_routes(CoverageFamily family)
    {Assert.Throws<ConfigurationException>(()=>DeploymentCoverageReads.Prepare(family,TestData.Office,CoverageReadKind.ServiceResults));}
    [Fact] public void Conditional_Access_targeting_is_in_the_policy_not_a_made_up_assignments_endpoint()
    {Assert.Throws<ConfigurationException>(()=>DeploymentCoverageReads.Prepare(CoverageFamily.ConditionalAccess,TestData.Office,CoverageReadKind.Assignments));}
    [Theory][InlineData("")][InlineData("00000000-0000-0000-0000-000000000000")][InlineData("../policies")][InlineData("https://graph.microsoft.com/users")][InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")][InlineData("{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}")]
    public void Untrusted_target_paths_and_non_D_GUIDs_are_refused(string id)
    {Assert.Throws<ConfigurationException>(()=>DeploymentCoverageReads.Prepare(CoverageFamily.ClassicCompliance,id,CoverageReadKind.Object));Assert.Throws<ConfigurationException>(()=>DeploymentCoverageReads.Group(id,true));}
    [Fact] public void Accepted_GUID_case_is_canonical_and_never_changes_the_namespace()
    {var r=DeploymentCoverageReads.Prepare(CoverageFamily.ClassicCompliance,"AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA",CoverageReadKind.ServiceResults);Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",r.TargetId);Assert.EndsWith("/deviceStatuses",r.Path);}
    [Theory][InlineData(false)][InlineData(true)]
    public void Assignment_group_reads_are_fixed_and_do_not_expand_transitive_or_hidden_membership(bool members)
    {
        var r=DeploymentCoverageReads.Group(TestData.Office,members);Assert.Equal("Group.Read.All",r.Scope);Assert.Equal(GraphApi.V1,r.Api);
        Assert.Equal("/groups/"+TestData.Office+(members?"/members?$select=id&$top=100":"?$select=id,displayName,groupTypes,membershipRuleProcessingState,visibility"),r.Path);
        Assert.DoesNotContain("transitive",r.Path);Assert.DoesNotContain("expand",r.Path);
    }
    [Fact] public void Unknown_families_kinds_or_API_versions_are_refused_without_fallback()
    {
        Assert.Throws<ConfigurationException>(()=>DeploymentCoverageReads.Prepare((CoverageFamily)999,TestData.Office,CoverageReadKind.Object));
        Assert.Throws<ConfigurationException>(()=>DeploymentCoverageReads.Prepare(CoverageFamily.Application,TestData.Office,(CoverageReadKind)999));
        Assert.Throws<ConfigurationException>(()=>DeploymentCoverageReads.Prepare(CoverageFamily.ClassicCompliance,TestData.Office,CoverageReadKind.Object,GraphApi.Beta));
    }
}
