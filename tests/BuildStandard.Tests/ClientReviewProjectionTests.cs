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
}
