using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CoverageGroupAdmissionTests
{
    private static JsonObject Group()=>new(){["id"]=TestData.Office,["displayName"]="Pilot",["groupTypes"]=new JsonArray(),["membershipRuleProcessingState"]=null,["visibility"]="Private"};
    [Theory][InlineData("Public")][InlineData("Private")]
    public void Explicit_static_visible_group_can_request_direct_members_but_not_a_tenant_denominator(string visibility)
    {var row=Group();row["visibility"]=visibility;var result=CoverageGroupAdmission.Read(row,TestData.Office);Assert.True(result.CanReadDirectMembers);Assert.Equal(TestData.Office,result.GroupId);Assert.Contains("service principals",result.Limitations);Assert.Contains("not effective targeting",result.Limitations);}
    [Theory][InlineData("DynamicMembership")][InlineData("futureType")]
    public void Dynamic_or_unknown_group_type_is_not_a_supported_static_population(string type)
    {var row=Group();row["groupTypes"]=new JsonArray(type);Assert.False(CoverageGroupAdmission.Read(row,TestData.Office).CanReadDirectMembers);}
    [Fact] public void Static_unified_group_remains_an_explicitly_limited_direct_population()
    {var row=Group();row["groupTypes"]=new JsonArray("Unified");Assert.True(CoverageGroupAdmission.Read(row,TestData.Office).CanReadDirectMembers);}
    [Theory][InlineData("HiddenMembership")][InlineData("futureVisibility")][InlineData("")][InlineData(null)]
    public void Hidden_or_unreturned_visibility_is_not_assumed_visible(string? visibility)
    {var row=Group();row["visibility"]=visibility;Assert.False(CoverageGroupAdmission.Read(row,TestData.Office).CanReadDirectMembers);}
    [Theory][InlineData("groupTypes")][InlineData("visibility")][InlineData("membershipRuleProcessingState")]
    public void Limited_metadata_is_unable_to_establish_supported_population(string property)
    {var row=Group();row.Remove(property);Assert.False(CoverageGroupAdmission.Read(row,TestData.Office).CanReadDirectMembers);}
    [Fact] public void Null_group_types_are_unknown_not_an_empty_static_array()
    {var row=Group();row["groupTypes"]=null;Assert.False(CoverageGroupAdmission.Read(row,TestData.Office).CanReadDirectMembers);}
    [Theory][InlineData("On")][InlineData("Paused")][InlineData("futureState")]
    public void A_returned_processing_state_never_establishes_a_static_group(string state)
    {var row=Group();row["membershipRuleProcessingState"]=state;Assert.False(CoverageGroupAdmission.Read(row,TestData.Office).CanReadDirectMembers);}
    [Theory][InlineData("groupTypes")][InlineData("visibility")][InlineData("membershipRuleProcessingState")][InlineData("id")]
    public void Malformed_metadata_is_not_coerced_into_static_population(string property)
    {var row=Group();row[property]=false;Assert.Throws<ConfigurationException>(()=>CoverageGroupAdmission.Read(row,TestData.Office));}
    [Fact] public void Wrong_or_missing_identity_cannot_authorise_a_different_group_read()
    {var row=Group();row["id"]="bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";Assert.Throws<TenantMismatchException>(()=>CoverageGroupAdmission.Read(row,TestData.Office));row.Remove("id");Assert.Throws<ConfigurationException>(()=>CoverageGroupAdmission.Read(row,TestData.Office));}
    [Fact] public void Duplicate_or_non_string_types_are_refused()
    {var row=Group();row["groupTypes"]=new JsonArray("Unified","Unified");Assert.Throws<ConfigurationException>(()=>CoverageGroupAdmission.Read(row,TestData.Office));row["groupTypes"]=new JsonArray(true);Assert.Throws<ConfigurationException>(()=>CoverageGroupAdmission.Read(row,TestData.Office));}
    [Fact] public void Display_name_is_never_group_identity_or_ownership()
    {var row=Group();row["displayName"]="BDIT managed baseline";var result=CoverageGroupAdmission.Read(row,TestData.Office);Assert.Contains("No ownership",result.Limitations);row["id"]=row["displayName"]!.GetValue<string>();Assert.Throws<ConfigurationException>(()=>CoverageGroupAdmission.Read(row,TestData.Office));}
}
