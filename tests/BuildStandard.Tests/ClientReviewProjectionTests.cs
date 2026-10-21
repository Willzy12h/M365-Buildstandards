using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Workflow;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ClientReviewProjectionTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly EvidenceStore _store;
    private readonly FixedClock _clock = new();
    private readonly TenantProfile _profile = TestData.Profile();
    public ClientReviewProjectionTests() => _store = new(_root.Paths, NullLog.Instance);
    public void Dispose() => _root.Dispose();
    private ClientReviewResult Review(ClientReviewEvidence? captured = null) => ClientReviewProjection.Project(_profile,
        captured ?? _store.LoadClientReviewEvidence(TestData.TenantA), _clock.UtcNow);
    private AssessmentResult Assessment(string at) => new() { Id=Guid.NewGuid().ToString(),TenantId=TestData.TenantA,
        AssessedAt=at,Release="test.1",SnapshotComplete=true,Findings=[] };
    private (TenantJob Job, StandardCatalogue Standard, TenantSnapshot Capture) CompleteJob()
    {
        var standard = TestData.Standard(); standard.IntegrityDigest=CanonicalJson.Sha256Hex(Encoding.UTF8.GetBytes(TestData.StandardJson));
        _root.WriteStandard("test.json",TestData.StandardJson); _root.WriteManifest();
        var capture=TestData.Snapshot(standard); _store.SaveSnapshot(capture);
        var workflow=new JobWorkflow(_store,_clock);
        var job=workflow.OpenJob(_profile,standard,JobIntention.NewBuild,"Engineer","","engineer@test.example",capture.Id);
        foreach(var control in new[]{"CA-001","CA-003","CMP-WIN-001","ID-001"})
            workflow.Record(TestData.TenantA,job.Id,_profile,standard,new ObservationRequest { SemanticId=control,ControlId=control,
                Status=ObservationStatus.Pass,Reason="Synthetic observation",ReviewDueAt=_clock.UtcNow.AddDays(30),SnapshotId=capture.Id },"engineer@test.example");
        return(job,standard,capture);
    }
    [Fact] public void Empty_history_has_no_invented_assessment_or_job()
    { var review=Review(); Assert.Null(review.LastAssessment);Assert.Empty(review.Jobs);Assert.Empty(review.Writes);Assert.True(review.WriteHistoryKnown); }
    [Fact] public void Latest_assessment_uses_instants_not_lexical_offsets()
    {
        var later=Assessment("2026-09-11T08:30:00Z"); _store.SaveAssessment(later);
        _store.SaveAssessment(Assessment("2026-09-11T10:00:00+02:00"));
        Assert.Equal(later.Id,Review().LastAssessment!.Id);
    }
    [Fact] public void Stored_assessment_collection_limits_survive_projection_and_both_reports()
    {
        var assessment=Assessment("2026-09-11T08:30:00Z");
        assessment.Limitations.Add("Synthetic collection stopped at its page limit");
        assessment.CollectionStatus.Add("policies","Partially collected");
        _store.SaveAssessment(assessment);
        var evidence=_store.LoadClientReviewEvidence(TestData.TenantA);
        var projected=ToolkitJson.Serialize(Review(evidence));
        Assert.Contains("Synthetic collection stopped",projected);
        var document=BuildStandard.Engine.Reports.ClientReviewDocuments.Create(_profile,evidence,_clock.UtcNow);
        foreach(var clientSummary in new[]{false,true})
        {
            var html=BuildStandard.Engine.Reports.ClientReviewDocuments.Html(document,clientSummary);
            Assert.Contains("Synthetic collection stopped",html);Assert.Contains("Partially collected",html);
        }
    }
    [Theory] [InlineData("limitations")] [InlineData("collectionStatus")]
    public void Malformed_collection_limits_are_diagnostics_not_a_crashed_client_scan(string member)
    {
        var assessment=Assessment("2026-09-11T08:30:00Z");_store.SaveAssessment(assessment);
        var file=Directory.GetFiles(Path.Combine(_store.TenantDirectory(TestData.TenantA),"assessments"),"*.json").Single();
        var node=ToolkitJson.ParseObject(File.ReadAllText(file));node[member]=null;File.WriteAllText(file,node.ToJsonString());
        var review=Review();Assert.Null(review.LastAssessment);Assert.NotEmpty(review.Diagnostics);
    }
    [Fact] public void Another_client_profile_cannot_label_captured_evidence()
    { var history=_store.LoadClientReviewEvidence(TestData.TenantA); var other=TestData.Profile(TestData.TenantB);
        Assert.Throws<ConfigurationException>(()=>ClientReviewProjection.Project(other,history,_clock.UtcNow)); }
    [Fact] public void A_good_assessment_does_not_hide_unreadable_write_history()
    {
        _store.SaveAssessment(Assessment("2026-09-11T08:30:00Z"));
        var dir=Path.Combine(_store.TenantDirectory(TestData.TenantA),"runs");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"broken.json"),"null");
        var review=Review(); Assert.NotNull(review.LastAssessment);Assert.False(review.WriteHistoryKnown);Assert.Single(review.Diagnostics);
    }
    [Theory] [InlineData("recovery")] [InlineData("reviewed-changes")] [InlineData("entra-laps")]
    public void Independent_unknown_writes_block_a_completed_job(string kind)
    {
        CompleteJob(); var id=Guid.NewGuid().ToString();
        if(kind=="recovery")_store.SaveRecoveryRun(new RecoveryRun{Id=id,TenantId=TestData.TenantA,ControlId="CA-001",StartedAt=Timestamps.Format(_clock.UtcNow),WriteAcceptance=WriteAcceptance.Unknown});
        if(kind=="reviewed-changes")_store.SaveReviewedChangeRun(new ReviewedChangeRun{Id=id,TenantId=TestData.TenantA,ControlId="CA-001",StartedAt=_clock.UtcNow,WriteAcceptance=WriteAcceptance.Unknown});
        if(kind=="entra-laps")_store.SaveEntraLapsRun(new EntraLapsRun{Id=id,TenantId=TestData.TenantA,StartedAt=_clock.UtcNow,WriteAcceptance=WriteAcceptance.Unknown});
        var review=Review();Assert.Equal(1,review.UnresolvedWrites);Assert.False(Assert.Single(review.Jobs).Complete);
    }
    [Theory] [InlineData("Future value")] [InlineData(WriteAcceptance.Unknown)]
    public void Unrecognised_write_acceptance_is_never_not_attempted(string acceptance)
    {
        _store.SaveRun(new DeploymentRun{Id=Guid.NewGuid().ToString(),TenantId=TestData.TenantA,StartedAt=Timestamps.Format(_clock.UtcNow),
            Results=[new RunResult{ControlId="CA-001",Status=ResultStatus.Error,PlannedAction=nameof(PlanAction.Create),WriteAcceptance=acceptance}]});
        var review=Review();Assert.Equal(1,review.UnresolvedWrites);Assert.NotEqual(WriteAcceptance.NotAttempted,Assert.Single(review.Writes).Acceptance);
    }
    [Fact] public void Complete_job_matches_existing_engine_semantics_from_captured_inputs()
    {
        var (job,standard,capture)=CompleteJob(); var live=JobCompletion.Build(_store,JobProjection.Build(_store,TestData.TenantA,job.Id,standard,_profile,_clock.UtcNow,capture),standard,_profile,_clock.UtcNow);
        var row=Assert.Single(Review().Jobs);Assert.True(live.Complete);Assert.True(row.Known);Assert.Equal(live.Claim,row.Claim);Assert.Equal(live.Verified,row.Verified);Assert.True(row.Complete);
    }
    [Theory] [InlineData("observations")] [InlineData("snapshots")] [InlineData("catalogue")]
    public void Missing_job_dependencies_never_make_completion_known(string section)
    {
        CompleteJob(); var dir=section=="catalogue"?_root.Paths.StandardsDirectory:Path.Combine(_store.TenantDirectory(TestData.TenantA),section);
        foreach(var file in Directory.GetFiles(dir,"*.json"))File.Delete(file);
        var job=Assert.Single(Review().Jobs);Assert.False(job.Known);Assert.False(job.Complete);Assert.Contains("unknown",job.Claim,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void Projection_does_not_return_to_the_live_store_after_capture()
    {
        CompleteJob();var history=_store.LoadClientReviewEvidence(TestData.TenantA);
        foreach(var file in Directory.GetFiles(Path.Combine(_store.TenantDirectory(TestData.TenantA),"observations"),"*.json"))File.Delete(file);
        Assert.True(Assert.Single(Review(history).Jobs).Complete);Assert.False(Assert.Single(Review().Jobs).Known);
        Assert.Throws<ConfigurationException>(()=>_store.RequireClientReviewCurrent(history));
    }
    [Theory] [InlineData("2026-09-01","Overdue")] [InlineData("2026-10-01","In date")] [InlineData("","Review date unknown")] [InlineData("invalid","Review date unknown")]
    public void Deviation_review_dates_are_explicit(string date,string expected)
    {
        _store.SaveDeviations(TestData.TenantA,[new Deviation{Id=Guid.NewGuid().ToString(),TenantId=TestData.TenantA,ControlId="CA-001",Owner="Engineer",ReviewBy=date}]);
        Assert.Equal(expected,Assert.Single(Review().Deviations).State);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void Unknown_run_or_row_status_keeps_client_write_history_unknown(bool runStatus)
    {
        var run=new DeploymentRun{Id=Guid.NewGuid().ToString(),TenantId=TestData.TenantA,StartedAt=Timestamps.Format(_clock.UtcNow),
            Status=runStatus?"Future state":RunStatus.Completed,
            Results=[new RunResult{ControlId="CA-001",Status=runStatus?ResultStatus.Completed:"Future state",
                WriteAcceptance=WriteAcceptance.Accepted,ObjectId=Guid.NewGuid().ToString(),Configuration=ConfigurationVerification.Pass}]};
        _store.SaveRun(run);var review=Review();Assert.False(review.WriteHistoryKnown);Assert.NotEmpty(review.Diagnostics);
    }
    [Fact] public void Exact_local_candidate_catalogue_is_a_captured_dependency_not_permanently_missing()
    {
        var json=TestData.StandardJson.Replace("test.1","import-test");var standard=BuildStandard.Engine.Standards.StandardsLoader.Parse(json,"import-test.json");
        standard.IntegrityDigest=CanonicalJson.Sha256Hex(Encoding.UTF8.GetBytes(json));
        var directory=Path.Combine(_store.TenantDirectory(TestData.TenantA),"catalogues");Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,"import-test.json");File.WriteAllText(file,json);
        File.WriteAllText(file+".integrity.json",ToolkitJson.Serialize(new {sha256=standard.IntegrityDigest,sourceDigest="synthetic",removedProperties=Array.Empty<string>()}));
        new JobWorkflow(_store,_clock).OpenJob(_profile,standard,JobIntention.NewBuild,"Engineer","","engineer@test.example");
        var captured=_store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Single(captured.Catalogues);Assert.DoesNotContain(captured.Diagnostics,d=>d.Section=="catalogue");
        Assert.True(Assert.Single(Review(captured).Jobs).Known);
        File.AppendAllText(file," ");Assert.Throws<ConfigurationException>(()=>_store.RequireClientReviewCurrent(captured));
    }

    private string AddLifecycleAttempt(bool outcome=false,string acceptance=WriteAcceptance.Unknown,bool readback=false)
    {
        var id=Guid.NewGuid().ToString("D");var plan=new System.Text.Json.Nodes.JsonObject
        { ["kind"]="lifecycle-plan",["schemaVersion"]=1,["id"]=id,["tenantId"]=TestData.TenantA,
          ["operation"]=ToolkitJson.ToNode(BuildStandard.Core.Safety.LifecycleActionRegistry.Create("disable-account",TestData.Office)),
          ["before"]=new System.Text.Json.Nodes.JsonObject { ["user"]=new System.Text.Json.Nodes.JsonObject { ["displayName"]="Synthetic lifecycle target" } },["integrityDigest"]="" };
        plan["integrityDigest"]=CanonicalJson.Sha256(plan);_store.SaveLifecyclePreview(plan);
        _store.SaveLifecycleIntent(new System.Text.Json.Nodes.JsonObject { ["kind"]="lifecycle-intent",["schemaVersion"]=1,["id"]=id,["tenantId"]=TestData.TenantA,["startedAt"]=Timestamps.Format(_clock.UtcNow),["planDigest"]=plan["integrityDigest"]!.DeepClone(),["integrityDigest"]="" });
        if(outcome)
        {
            var after=new System.Text.Json.Nodes.JsonObject { ["kind"]="lifecycle-before",["schemaVersion"]=1,["id"]=Guid.NewGuid().ToString("D"),["tenantId"]=TestData.TenantA,["complete"]=true,["integrityDigest"]="" };
            after["integrityDigest"]=CanonicalJson.Sha256(after);if(readback)_store.SaveLifecycleBefore(after);
            _store.SaveLifecycleOutcome(new System.Text.Json.Nodes.JsonObject { ["kind"]="lifecycle-run",["schemaVersion"]=1,["id"]=id,["tenantId"]=TestData.TenantA,["startedAt"]=Timestamps.Format(_clock.UtcNow),["planDigest"]=plan["integrityDigest"]!.DeepClone(),["writeAcceptance"]=acceptance,["verification"]="Pass",["status"]="Completed",["afterId"]=after["id"]!.DeepClone(),["afterDigest"]=after["integrityDigest"]!.DeepClone(),["integrityDigest"]="" });
        }
        return id;
    }
    [Fact]public void Lifecycle_intent_only_history_blocks_portfolio_job_completion_and_is_cited()
    {
        CompleteJob();var id=AddLifecycleAttempt();var evidence=_store.LoadClientReviewEvidence(TestData.TenantA);var review=Review(evidence);
        Assert.True(review.WriteHistoryKnown,ToolkitJson.Serialize(review.Diagnostics));Assert.Equal(1,review.UnresolvedWrites);Assert.Equal(id,Assert.Single(review.Writes).RunId);Assert.False(Assert.Single(review.Jobs).Complete);
        Assert.Contains(evidence.Sources,source=>source.Section=="lifecycle"&&source.RelativePath.EndsWith("intent-"+id+".json",StringComparison.Ordinal));
    }
    [Theory][InlineData(WriteAcceptance.Accepted,true,false)][InlineData(WriteAcceptance.Accepted,false,true)][InlineData(WriteAcceptance.Unknown,true,true)]
    public void Lifecycle_portfolio_uses_original_acceptance_and_intact_readback_not_only_a_pass_label(string acceptance,bool readback,bool blocked)
    {
        CompleteJob();AddLifecycleAttempt(true,acceptance,readback);var review=Review();
        if(readback)Assert.True(review.WriteHistoryKnown,ToolkitJson.Serialize(review.Diagnostics));
        Assert.Equal(!blocked,Assert.Single(review.Jobs).Complete);
        if(acceptance==WriteAcceptance.Unknown)Assert.Equal(1,review.UnresolvedWrites);
        if(acceptance==WriteAcceptance.Accepted&&!readback)Assert.False(review.WriteHistoryKnown);
    }
    [Fact]public void Occupied_lifecycle_history_is_unknown_in_the_client_portfolio()
    {CompleteJob();File.WriteAllText(Path.Combine(_store.TenantDirectory(TestData.TenantA),"lifecycle"),"Preserve marker");var review=Review();Assert.False(review.WriteHistoryKnown);Assert.False(Assert.Single(review.Jobs).Complete);Assert.Contains(review.Diagnostics,d=>d.Section=="lifecycle");}
    [Fact]public void New_lifecycle_history_invalidates_an_old_client_review_export_capture()
    {var captured=_store.LoadClientReviewEvidence(TestData.TenantA);AddLifecycleAttempt();Assert.ThrowsAny<ToolkitException>(()=>_store.RequireClientReviewCurrent(captured));}

    [Fact]public async Task Actual_offline_clients_cli_cannot_hide_an_unresolved_lifecycle_attempt()
    {
        CompleteJob();_store.SaveProfiles([_profile]);AddLifecycleAttempt();
        var result=await RegisteredReportCliTests.Run("clients","--root",_root.Root);Assert.True(result.Exit==0,result.Error+result.Output);
        var row=System.Text.Json.Nodes.JsonNode.Parse(result.Output)!["clients"]![0]!;
        Assert.Equal(1,row["unresolvedWrites"]!.GetValue<int>());Assert.Equal(0,row["completeJobs"]!.GetValue<int>());
    }

    [Fact]public void Lifecycle_portfolio_projection_remains_frozen_when_later_known_outcome_is_saved()
    {
        CompleteJob();var id=AddLifecycleAttempt();var captured=_store.LoadClientReviewEvidence(TestData.TenantA);
        var plan=_store.ReadLifecycleRecord(TestData.TenantA,"plan",id).GetDocument();
        _store.SaveLifecycleOutcome(new System.Text.Json.Nodes.JsonObject { ["kind"]="lifecycle-run",["schemaVersion"]=1,["id"]=id,["tenantId"]=TestData.TenantA,["startedAt"]=Timestamps.Format(_clock.UtcNow),["planDigest"]=plan["integrityDigest"]!.DeepClone(),["writeAcceptance"]=WriteAcceptance.NotAttempted,["verification"]="Unknown",["status"]="Stopped",["integrityDigest"]="" });
        Assert.Equal(1,Review(captured).UnresolvedWrites);Assert.Equal(0,Review().UnresolvedWrites);Assert.True(Assert.Single(Review().Jobs).Complete);
        Assert.ThrowsAny<ToolkitException>(()=>_store.RequireClientReviewCurrent(captured));
    }
    [Fact]public void Foreign_lifecycle_record_is_a_portfolio_diagnostic_never_an_empty_safe_history()
    {
        CompleteJob();var id=AddLifecycleAttempt();var path=Path.Combine(_store.TenantDirectory(TestData.TenantA),"lifecycle","intent-"+id+".json");
        var node=ToolkitJson.ParseObject(File.ReadAllText(path));node["tenantId"]=TestData.TenantB;node["integrityDigest"]="";node["integrityDigest"]=CanonicalJson.Sha256(node);File.WriteAllText(path,node.ToJsonString());
        var review=Review();Assert.False(review.WriteHistoryKnown);Assert.False(Assert.Single(review.Jobs).Complete);Assert.Contains(review.Diagnostics,problem=>problem.Section=="lifecycle");
    }

}
