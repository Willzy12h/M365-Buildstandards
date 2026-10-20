using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CoverageMembershipProjectionTests
{
    private static CoverageGroupAdmission Group()=>new(TestData.Office,true,"Synthetic supported static group");
    private static JsonObject Member(string id,string? type)=>new(){["id"]=id,["@odata.type"]=type};
    private static CoverageSourceCapture Source(CoverageSourceState state,params JsonObject[] rows)=>new(state,rows,[],"Synthetic source");
    [Fact] public void Complete_supported_user_membership_can_observe_present_and_absent_in_the_same_namespace()
    {var result=CoverageMembershipProjection.Project(Group(),Source(CoverageSourceState.Complete,Member(TestData.Operator,"#microsoft.graph.user")),[TestData.Operator,TestData.Emergency],[]);Assert.Equal(new[]{"Observed present","Observed absent"},result.Pilots.Select(p=>p.Observation));Assert.All(result.Pilots,p=>Assert.Equal("Directory user object ID",p.IdentityNamespace));Assert.Contains("not effective targeting",result.Limitations);Assert.Contains("service principals",result.Limitations);}
    [Fact] public void Directory_device_membership_never_becomes_an_Intune_device_join()
    {var result=CoverageMembershipProjection.Project(Group(),Source(CoverageSourceState.Complete,Member(TestData.Mam,"#microsoft.graph.device")),[],[TestData.Mam]);var member=Assert.Single(result.Members);Assert.Equal("Directory device object ID",member.IdentityNamespace);Assert.Contains("not an Intune",member.Detail);Assert.Equal("Observed present",Assert.Single(result.Pilots).Observation);}
    [Theory][InlineData(CoverageSourceState.Partial)][InlineData(CoverageSourceState.Failed)][InlineData(CoverageSourceState.Stopped)][InlineData(CoverageSourceState.ContextChanged)]
    public void Unfinished_or_other_client_sources_cannot_establish_pilot_presence_or_absence(CoverageSourceState state)
    {var result=CoverageMembershipProjection.Project(Group(),Source(state,Member(TestData.Operator,"#microsoft.graph.user")),[TestData.Operator,TestData.Emergency],[]);Assert.All(result.Pilots,p=>Assert.Equal("Unknown",p.Observation));Assert.Contains(state.ToString(),result.PopulationState);}
    [Theory][InlineData("#microsoft.graph.group")][InlineData("#microsoft.graph.servicePrincipal")][InlineData("#microsoft.graph.futureType")][InlineData(null)]
    public void Nested_unsupported_or_unknown_member_types_do_not_establish_a_supported_absence(string? type)
    {var result=CoverageMembershipProjection.Project(Group(),Source(CoverageSourceState.Complete,Member(TestData.Office,type)),[TestData.Emergency],[]);Assert.Equal("Unknown",Assert.Single(result.Pilots).Observation);Assert.Equal(type,Assert.Single(result.Members).RawType);}
    [Fact] public void Mixed_users_and_devices_preserve_rows_but_absence_is_unknown()
    {var result=CoverageMembershipProjection.Project(Group(),Source(CoverageSourceState.Complete,Member(TestData.Operator,"#microsoft.graph.user"),Member(TestData.Mam,"#microsoft.graph.device")),[TestData.Emergency],[]);Assert.Equal(2,result.Members.Count);Assert.Equal("Unknown",Assert.Single(result.Pilots).Observation);}
    [Fact] public void Same_GUID_in_wrong_namespace_never_matches_a_pilot()
    {var result=CoverageMembershipProjection.Project(Group(),Source(CoverageSourceState.Complete,Member(TestData.Operator,"#microsoft.graph.device")),[TestData.Operator],[]);Assert.Equal("Unknown",Assert.Single(result.Pilots).Observation);}
    [Fact] public void Complete_explicit_empty_static_result_can_establish_only_selected_subject_absence()
    {var result=CoverageMembershipProjection.Project(Group(),Source(CoverageSourceState.Complete),[TestData.Operator],[]);Assert.Equal("Observed absent",Assert.Single(result.Pilots).Observation);Assert.Contains("not a tenant",result.Limitations);}
    [Fact] public void Unsupported_group_admission_does_not_become_supported_just_because_members_were_supplied()
    {var result=CoverageMembershipProjection.Project(new(TestData.Office,false,"Hidden or dynamic population"),Source(CoverageSourceState.Complete,Member(TestData.Operator,"#microsoft.graph.user")),[TestData.Operator],[]);Assert.Equal("Unknown",Assert.Single(result.Pilots).Observation);}
    [Fact] public void Display_names_and_UPNs_are_not_used_to_match_selected_subjects()
    {var row=Member(TestData.Emergency,"#microsoft.graph.user");row["displayName"]=TestData.Operator;row["userPrincipalName"]=TestData.Operator;var result=CoverageMembershipProjection.Project(Group(),Source(CoverageSourceState.Complete,row),[TestData.Operator],[]);Assert.Equal("Observed absent",Assert.Single(result.Pilots).Observation);}
    [Fact] public void Unknown_member_type_is_not_matched_even_when_the_GUID_matches()
    {var result=CoverageMembershipProjection.Project(Group(),Source(CoverageSourceState.Complete,Member(TestData.Operator,null)),[TestData.Operator],[]);Assert.Equal("Unknown",Assert.Single(result.Pilots).Observation);}
    [Fact] public void Invalid_duplicate_or_malformed_ids_are_refused()
    {Assert.Throws<ConfigurationException>(()=>CoverageMembershipProjection.Project(Group(),Source(CoverageSourceState.Complete,Member("name","#microsoft.graph.user")),[],[]));Assert.Throws<ConfigurationException>(()=>CoverageMembershipProjection.Project(Group(),Source(CoverageSourceState.Complete),[TestData.Operator,TestData.Operator],[]));Assert.Throws<ConfigurationException>(()=>CoverageMembershipProjection.Project(Group(),Source(CoverageSourceState.Complete,Member(TestData.Operator,"#microsoft.graph.user"),Member(TestData.Operator,"#microsoft.graph.user")),[],[]));}
}
