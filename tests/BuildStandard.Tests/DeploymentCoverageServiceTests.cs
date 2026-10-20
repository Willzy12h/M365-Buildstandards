using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class DeploymentCoverageServiceTests
{
    private sealed class Fixture
    {
        public DeploymentCoverageSelectionTests.Fixture Data {get;}=new();
        public List<string> Reads {get;}=[];
        public bool Current=true;
        public Func<PreparedCoverageRead,CancellationToken,Task<CoverageReadResponse>>? Override;
        public string Assignments="{\"value\":[]}";
        public string Members="{\"value\":[{\"id\":\""+TestData.Operator+"\",\"@odata.type\":\"#microsoft.graph.user\"}]}";
        public string Group="{\"id\":\""+TestData.Emergency+"\",\"groupTypes\":[],\"membershipRuleProcessingState\":null,\"visibility\":\"Private\"}";
        public Task<CoverageReadResponse> Read(PreparedCoverageRead request,CancellationToken ct)
        {
            Reads.Add(request.Path);if(Override is not null)return Override(request,ct);
            var body=request.Path.Contains("/members?")?Members:request.Path.StartsWith("/groups/")?Group:request.Path.EndsWith("/assignments")?Assignments:request.Path.EndsWith("/deviceStatuses")?"{\"value\":[{\"id\":\"opaque-report-row\",\"status\":\"compliant\",\"lastReportedDateTime\":\"2026-10-19T11:00:00Z\"}]}":"{\"id\":\""+TestData.Office+"\",\"displayName\":\"CMP - Core - Windows\"}";
            return Task.FromResult(CoverageResponseCodec.Read(Encoding.UTF8.GetBytes(body),request));
        }
        public Task<DeploymentCoverageCapture> Capture(CancellationToken ct=default)=>new DeploymentCoverageService().CaptureAsync(Data.Prepare(),Data.Session,Read,()=>Current,ct);
        public Fixture(){Data.Session.Scopes.Add("Group.Read.All");}
        public void GroupAssignment()=>Assignments="{\"value\":[{\"id\":\"a1\",\"target\":{\"@odata.type\":\"#microsoft.graph.groupAssignmentTarget\",\"groupId\":\""+TestData.Emergency+"\",\"deviceAndAppManagementAssignmentFilterType\":\"none\",\"deviceAndAppManagementAssignmentFilterId\":null}}]}";
    }
    [Fact] public async Task Selected_compliance_report_collects_fixed_sources_and_keeps_service_rows_separate_from_membership()
    {var f=new Fixture();f.GroupAssignment();var result=await f.Capture();Assert.True(result.IsCurrentAtCompletion);Assert.Equal(5,result.Sources.Count);Assert.Single(result.Assignments);Assert.Single(result.Groups);Assert.Single(result.ComplianceObservations);Assert.Equal("opaque-report-row",result.ComplianceObservations[0].ReportRowId);Assert.Contains("no authoritative managed-device",result.ComplianceObservations[0].IdentityMeaning);Assert.Equal("Observed present",result.Groups[0].Membership.Pilots.Single(p=>p.ObjectId==TestData.Operator).Observation);Assert.Contains("not effective",result.Limitations);Assert.DoesNotContain(f.Reads,p=>p.Contains("transitive")||p.Contains("installStatuses"));}
    [Fact] public async Task Explicit_unassigned_is_separate_from_missing_or_failed_assignments()
    {var f=new Fixture();var result=await f.Capture();Assert.Equal("Observed unassigned",result.CandidateState);Assert.Empty(result.Assignments);Assert.Empty(result.Groups);Assert.Equal(3,f.Reads.Count);f=new();f.Override=(r,_)=>r.Path.EndsWith("/assignments")?Task.FromException<CoverageReadResponse>(new HttpRequestException()):Task.FromResult(CoverageResponseCodec.Read(Encoding.UTF8.GetBytes(r.IsCollection?"{\"value\":[]}":"{\"id\":\""+TestData.Office+"\"}"),r));result=await f.Capture();Assert.Equal("Assignment state unknown",result.CandidateState);Assert.Contains(result.Sources,s=>s.Key=="assignments"&&s.Capture.State==CoverageSourceState.Failed);}
    [Fact] public async Task Hidden_or_dynamic_group_never_starts_a_member_read()
    {var f=new Fixture();f.GroupAssignment();f.Group=f.Group.Replace("\"Private\"","\"HiddenMembership\"");var result=await f.Capture();Assert.DoesNotContain(f.Reads,p=>p.Contains("/members?"));Assert.All(result.Groups[0].Membership.Pilots,p=>Assert.Equal("Unknown",p.Observation));}
    [Fact] public async Task Thirty_two_group_limit_keeps_assignments_but_marks_unread_populations_explicit()
    {var f=new Fixture();f.Assignments="{\"value\":["+string.Join(',',Enumerable.Range(1,33).Select(i=>"{\"id\":\"a"+i+"\",\"target\":{\"@odata.type\":\"#microsoft.graph.groupAssignmentTarget\",\"groupId\":\""+Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-"+i.ToString("D12"))+"\"}}"))+"]}";f.Override=(r,_)=>Task.FromResult(CoverageResponseCodec.Read(Encoding.UTF8.GetBytes(r.Path.EndsWith("/assignments")?f.Assignments:r.IsCollection?"{\"value\":[]}":"{\"id\":\""+r.TargetId+"\",\"groupTypes\":[],\"membershipRuleProcessingState\":null,\"visibility\":\"Private\"}"),r));var result=await f.Capture();Assert.Equal(33,result.Assignments.Count);Assert.Equal(32,f.Reads.Count(p=>p.StartsWith("/groups/")&&!p.Contains("/members?")));Assert.Contains("32",result.Limitations);Assert.False(result.SupportedReadsComplete);}
    [Fact] public async Task Context_change_after_a_read_retains_originals_but_stops_further_reads()
    {var f=new Fixture();f.Override=(r,_)=>{f.Current=false;return Task.FromResult(CoverageResponseCodec.Read(Encoding.UTF8.GetBytes("{\"id\":\""+TestData.Office+"\"}"),r));};var result=await f.Capture();Assert.False(result.IsCurrentAtCompletion);Assert.Single(f.Reads);Assert.Single(result.Sources[0].Capture.OriginalPages);Assert.False(result.SupportedReadsComplete);}
    [Theory][InlineData("tenant")][InlineData("account")][InlineData("mode")][InlineData("scopes")]
    public async Task A_frozen_selection_cannot_be_used_with_a_changed_connection(string defect)
    {var f=new Fixture();var selection=f.Data.Prepare();switch(defect){case "tenant":f.Data.Session.TenantId=TestData.TenantB;break;case "account":f.Data.Session.AccountObjectId=TestData.Office;break;case "mode":f.Data.Session.Mode=SessionMode.Deployment;break;case "scopes":f.Data.Session.Scopes.Add("User.Read.All");break;}await Assert.ThrowsAnyAsync<ToolkitException>(()=>new DeploymentCoverageService().CaptureAsync(selection,f.Data.Session,f.Read,()=>true,CancellationToken.None));Assert.Empty(f.Reads);}
    [Fact] public async Task Failed_object_read_does_not_turn_into_a_missing_candidate_or_trigger_other_reads()
    {var f=new Fixture();f.Override=(_,_)=>Task.FromException<CoverageReadResponse>(new HttpRequestException());var result=await f.Capture();Assert.Equal("Candidate state unknown",result.CandidateState);Assert.Single(f.Reads);Assert.False(result.SupportedReadsComplete);}
    [Fact] public async Task Non_cooperative_read_is_bounded_by_the_capture_deadline()
    {var f=new Fixture();var clock=new ConnectAcquisitionBudgetTests.ManualTime();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var late=new TaskCompletionSource<CoverageReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);f.Override=(_,_)=>{entered.SetResult();return late.Task;};var task=new DeploymentCoverageService(clock).CaptureAsync(f.Data.Prepare(),f.Data.Session,f.Read,()=>true,CancellationToken.None);await entered.Task;clock.Advance(TimeSpan.FromMinutes(5));try{var result=await task.WaitAsync(TimeSpan.FromSeconds(1));Assert.False(result.SupportedReadsComplete);Assert.Single(f.Reads);}finally{late.TrySetCanceled();}}
    [Fact] public async Task Malformed_assignment_target_is_not_a_complete_no_group_result()
    {var f=new Fixture();f.Assignments="{\"value\":[{\"id\":\"bad\",\"target\":false}]}";var result=await f.Capture();Assert.False(result.SupportedReadsComplete);Assert.Contains("assignment",result.Limitations);Assert.DoesNotContain(f.Reads,p=>p.StartsWith("/groups/"));}
    [Fact] public async Task Malformed_member_type_keeps_original_sources_and_marks_population_unknown()
    {var f=new Fixture();f.GroupAssignment();f.Members="{\"value\":[{\"id\":\""+TestData.Operator+"\",\"@odata.type\":false}]}";var result=await f.Capture();Assert.False(result.SupportedReadsComplete);Assert.Contains(result.Sources,s=>s.Key.EndsWith(":members")&&s.Capture.OriginalPages.Count==1);Assert.Contains("member",result.Limitations);}
    [Fact] public async Task An_outer_capture_deadline_is_not_reported_as_an_engineer_stop()
    {var f=new Fixture();var clock=new ConnectAcquisitionBudgetTests.ManualTime();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var late=new TaskCompletionSource<CoverageReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);f.Override=(_,_)=>{entered.SetResult();return late.Task;};var task=new DeploymentCoverageService(clock).CaptureAsync(f.Data.Prepare(),f.Data.Session,f.Read,()=>true,CancellationToken.None);await entered.Task;clock.Advance(TimeSpan.FromMinutes(5));try{var result=await task.WaitAsync(TimeSpan.FromSeconds(1));Assert.Equal(CoverageSourceState.Failed,result.Sources[0].Capture.State);Assert.Contains("deadline",result.Sources[0].Capture.Detail);}finally{late.TrySetCanceled();}}

}
