using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CoverageAssignmentObservationTests
{
    private static JsonObject Row(string type="groupAssignmentTarget")=>new(){["id"]="assignment-row",["intent"]="required",["target"]=new JsonObject{["@odata.type"]="#microsoft.graph."+type,["groupId"]=TestData.Office,["deviceAndAppManagementAssignmentFilterType"]="none",["deviceAndAppManagementAssignmentFilterId"]=null}};
    [Theory][InlineData("groupAssignmentTarget","Configured group")][InlineData("exclusionGroupAssignmentTarget","Configured group exclusion")]
    public void Exact_group_target_is_observation_only_even_without_a_filter(string type,string expected)
    {var r=CoverageAssignmentObservation.Read(Row(type));Assert.Equal(expected,r.TargetMeaning);Assert.Equal(TestData.Office,r.GroupObjectId);Assert.True(r.CanObserveGroup);Assert.True(r.UnfilteredTargetVerified);Assert.Equal("required",r.RawIntent);Assert.Contains("not effective targeting",r.Detail);Assert.Contains("not installation",r.Detail);}
    [Theory][InlineData("allDevicesAssignmentTarget","Configured all devices (Intune built-in)")][InlineData("allLicensedUsersAssignmentTarget","Configured all users (Intune built-in)")]
    public void Built_in_populations_are_not_enumerated_complete_estates(string type,string expected)
    {var row=Row(type);row["target"]!.AsObject().Remove("groupId");var r=CoverageAssignmentObservation.Read(row);Assert.Equal(expected,r.TargetMeaning);Assert.False(r.CanObserveGroup);Assert.Null(r.GroupObjectId);Assert.Contains("not an enumerated estate",r.Detail);}
    [Theory][InlineData("include")][InlineData("exclude")][InlineData("futureFilter")][InlineData(null)]
    public void Non_none_or_unknown_filter_does_not_establish_effective_targeting(string? filter)
    {var row=Row();row["target"]!["deviceAndAppManagementAssignmentFilterType"]=filter;var r=CoverageAssignmentObservation.Read(row);Assert.False(r.UnfilteredTargetVerified);Assert.True(r.CanObserveGroup);Assert.Contains("filter",r.Detail);}
    [Theory][InlineData("deviceAndAppManagementAssignmentFilterType")][InlineData("deviceAndAppManagementAssignmentFilterId")]
    public void Omitted_filter_metadata_is_unknown_not_none(string property)
    {var row=Row();row["target"]!.AsObject().Remove(property);Assert.False(CoverageAssignmentObservation.Read(row).UnfilteredTargetVerified);}
    [Fact] public void A_filter_ID_with_none_is_not_unfiltered_and_raw_identity_is_preserved()
    {var row=Row();row["target"]!["deviceAndAppManagementAssignmentFilterId"]=TestData.Operator;var r=CoverageAssignmentObservation.Read(row);Assert.False(r.UnfilteredTargetVerified);Assert.Equal(TestData.Operator,r.RawFilterId);}
    [Fact] public void Future_target_properties_require_review_even_with_a_known_type()
    {var row=Row();row["target"]!["futureCondition"]=true;Assert.False(CoverageAssignmentObservation.Read(row).UnfilteredTargetVerified);}
    [Theory][InlineData("futureAssignmentTarget")][InlineData("deviceAndAppManagementAssignmentTarget")]
    public void Future_or_base_target_type_is_unknown_not_an_all_users_target(string type)
    {var r=CoverageAssignmentObservation.Read(Row(type));Assert.Equal("Unknown target",r.TargetMeaning);Assert.False(r.CanObserveGroup);Assert.False(r.UnfilteredTargetVerified);}
    [Theory][InlineData("")][InlineData("friendly name")][InlineData("00000000-0000-0000-0000-000000000000")][InlineData(null)]
    public void Unusable_group_identity_never_becomes_a_read_target(string? id)
    {var row=Row();row["target"]!["groupId"]=id;var r=CoverageAssignmentObservation.Read(row);Assert.False(r.CanObserveGroup);Assert.Null(r.GroupObjectId);Assert.Equal(id,r.RawGroupId);Assert.Contains("Unknown",r.TargetMeaning);}
    [Fact] public void Missing_target_is_unknown_while_missing_row_identity_is_refused()
    {var row=Row();row.Remove("target");Assert.Equal("Unknown target",CoverageAssignmentObservation.Read(row).TargetMeaning);row.Remove("id");Assert.Throws<ConfigurationException>(()=>CoverageAssignmentObservation.Read(row));}
    [Theory][InlineData("intent")][InlineData("target")][InlineData("groupId")][InlineData("@odata.type")][InlineData("deviceAndAppManagementAssignmentFilterType")][InlineData("deviceAndAppManagementAssignmentFilterId")]
    public void Wrong_wire_types_are_not_stringified_or_coerced(string property)
    {var row=Row();if(property is "intent" or "target")row[property]=false;else row["target"]![property]=false;Assert.Throws<ConfigurationException>(()=>CoverageAssignmentObservation.Read(row));}
    [Fact] public void Unknown_intent_remains_raw_and_never_becomes_required_or_installed()
    {var row=Row();row["intent"]="futureIntent";var r=CoverageAssignmentObservation.Read(row);Assert.Equal("futureIntent",r.RawIntent);Assert.Contains("not installation",r.Detail);}
}
