using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Standards;
using BDIT.TenantToolkit.Engine.Workflow;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>INT-049 completion projection: only accepted outcomes and approved departures satisfy a requirement.</summary>
public sealed class JobCompletionTests : IDisposable
{
    private const string Policy = "aaaaaaaa-1111-4111-8111-000000000009";
    private const string Actor = "engineer@test.example";
    private static readonly string[] Controls = { "CA-001", "CA-003", "CMP-WIN-001", "ID-001" };
    private readonly TempRoot _root = new();
    private readonly FixedClock _clock = new();
    private readonly EvidenceStore _store;
    private readonly JobWorkflow _workflow;
    private readonly StandardCatalogue _standard = TestData.Standard();
    private readonly TenantProfile _profile = TestData.Profile();
    private readonly TenantSnapshot _capture;
    private readonly TenantJob _job;

    public JobCompletionTests()
    {
        _standard.IntegrityDigest = CanonicalJson.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(TestData.StandardJson));
        _store = new EvidenceStore(_root.Paths, NullLog.Instance);
        _workflow = new JobWorkflow(_store, _clock);
        _capture = TestData.Snapshot(_standard);
        _capture.Collections["conditionalAccess"].Items.Add(TestData.ConditionalAccessPolicy(Policy, "Require MFA", "enabled", new[] { TestData.Emergency }));
        _store.SaveSnapshot(_capture);
        _job = _workflow.OpenJob(_profile, _standard, JobIntention.NewBuild, "Owner Name", "", Actor, _capture.Id);
    }

    public void Dispose() => _root.Dispose();

    private TenantObservation Observe(string control, string status = ObservationStatus.Pass, string? supersedes = null, bool evidence = true) =>
        _workflow.Record(TestData.TenantA, _job.Id, _profile, _standard, new ObservationRequest
        {
            SemanticId = control, ControlId = control, Status = status, Reason = "Checked in the portal.",
            ReviewDueAt = _clock.UtcNow.AddDays(90), SnapshotId = evidence ? _capture.Id : null, SupersedesId = supersedes
        }, Actor);

    private TenantDisposition Decide(string control, string decision, string? deviation = null) =>
        _workflow.Decide(TestData.TenantA, _job.Id, _profile, _standard, new DispositionRequest
        {
            SemanticId = control, ControlId = control, Decision = decision, Owner = "Client security lead", Reason = "Agreed with the client.",
            ReviewDueAt = _clock.UtcNow.AddDays(90), SnapshotId = _capture.Id,
            ObservedObjectIds = decision == DispositionDecision.RetainExternalCoverage ? new() { Policy } : new(), DeviationId = deviation
        }, Actor);

    private Deviation Deviation(string control, DeviationKind kind, int days = 30)
    {
        var deviation = new Deviation
        {
            Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, ControlId = control, Kind = kind, Reason = "Agreed",
            ApprovedBy = "Client CISO", ReviewBy = _clock.UtcNow.AddDays(days).ToString("yyyy-MM-dd")
        };
        _store.SaveDeviations(TestData.TenantA, _store.LoadDeviations(TestData.TenantA).Append(deviation).ToList());
        return deviation;
    }

    private JobCompletion Completion(StandardCatalogue? standard = null)
    {
        var projection = JobProjection.Build(_store, TestData.TenantA, _job.Id, standard ?? _standard, _profile, _clock.UtcNow, _capture);
        return JobCompletion.Build(_store, projection, standard ?? _standard, _profile, _clock.UtcNow);
    }

    private RequirementCompletion Requirement(JobCompletion completion, string control) => completion.Requirements.Single(r => r.InstanceKey == control);

    [Fact]
    public void A_new_job_lists_every_applicable_requirement_as_outstanding()
    {
        var completion = Completion();

        Assert.Equal(Controls, completion.Requirements.Select(r => r.InstanceKey).ToArray());
        Assert.All(completion.Requirements, r => Assert.True(r.Outstanding));
        Assert.False(completion.Complete);
        Assert.StartsWith("Not complete: 4 of 4", completion.Claim);
    }

    [Fact]
    public void Accepted_passes_for_every_requirement_complete_the_job()
    {
        foreach (var control in Controls) Observe(control);
        var completion = Completion();

        Assert.True(completion.Complete);
        Assert.Equal(4, completion.Verified);
        Assert.Equal("Complete: all 4 requirement(s) verified.", completion.Claim);
    }

    [Fact]
    public void Accepted_passes_do_not_complete_a_job_with_an_unresolved_tenant_write()
    {
        foreach (var control in Controls) Observe(control);
        var run = new DeploymentRun
        {
            Id = Guid.NewGuid().ToString(), PlanId = Guid.NewGuid().ToString(), TenantId = TestData.TenantA,
            StartedAt = Timestamps.Format(_clock.UtcNow), Status = RunStatus.ReviewRequired,
            Results = [new RunResult { ControlId = "CA-001", PlannedAction = nameof(PlanAction.Create),
                Status = ResultStatus.Error, WriteAcceptance = WriteAcceptance.Unknown,
                Configuration = ConfigurationVerification.Unknown }]
        };
        _store.SaveRun(run);

        var completion = Completion();

        Assert.False(completion.Complete);
        Assert.Contains(completion.Blockers, reason => reason.Contains(run.Id));
        Assert.StartsWith("Not complete", completion.Claim);
    }

    [Fact]
    public void A_second_semantic_identity_cannot_hide_a_failed_outcome_for_the_same_requirement()
    {
        foreach (var control in Controls) Observe(control);
        Assert.Throws<BDIT.TenantToolkit.Core.SafetyViolationException>(() => _workflow.Record(
            TestData.TenantA, _job.Id, _profile, _standard, new ObservationRequest
            {
                SemanticId = "different.requirement", ControlId = "CA-001", Status = ObservationStatus.Fail,
                Reason = "The current requirement failed.", ReviewDueAt = _clock.UtcNow.AddDays(30),
                SnapshotId = _capture.Id
            }, Actor));
    }

    [Fact]
    public void Previously_stored_conflicting_identities_block_completion_without_rewriting_history()
    {
        foreach (var control in Controls) Observe(control);
        var job = _store.RequireJob(TestData.TenantA, _job.Id);
        var original = _store.LoadObservation(TestData.TenantA, job.ObservationIds[0])!;
        var originalId = original.Id;
        original.Id = Guid.NewGuid().ToString();
        original.SemanticId = "different.requirement";
        original.Status = ObservationStatus.Fail;
        _store.CreateObservation(original);
        job.ObservationIds.Add(original.Id);
        _store.ReplaceJob(job);

        var completion = Completion();

        Assert.False(completion.Complete);
        Assert.Contains(completion.Blockers, reason => reason.Contains("conflicting outcome identities"));
        Assert.Equal(ObservationStatus.Pass, _store.LoadObservation(TestData.TenantA, originalId)!.Status);
        Assert.Equal(ObservationStatus.Fail, _store.LoadObservation(TestData.TenantA, original.Id)!.Status);
    }

    [Theory]
    [InlineData(WriteAcceptance.NotAttempted, ResultStatus.Error, true)]
    [InlineData(WriteAcceptance.Unknown, ResultStatus.NotRun, true)]
    [InlineData(WriteAcceptance.Unknown, ResultStatus.Error, false)]
    public void Completion_distinguishes_unattempted_reads_from_uncertain_writes(string acceptance, string status, bool complete)
    {
        foreach (var control in Controls) Observe(control);
        _store.SaveRun(new DeploymentRun
        {
            Id = Guid.NewGuid().ToString(), PlanId = Guid.NewGuid().ToString(), TenantId = TestData.TenantA,
            StartedAt = Timestamps.Format(_clock.UtcNow), Status = RunStatus.ReviewRequired,
            Results = [new RunResult { ControlId = "CA-001", PlannedAction = nameof(PlanAction.Create),
                Status = status, WriteAcceptance = acceptance, Configuration = ConfigurationVerification.Unknown }]
        });
        Assert.Equal(complete, Completion().Complete);
    }

    [Theory]
    [InlineData(ObservationStatus.Fail)]
    [InlineData(ObservationStatus.Unknown)]
    [InlineData(ObservationStatus.Pending)]
    public void Fail_unknown_and_pending_never_satisfy_a_requirement(string status)
    {
        foreach (var control in Controls) Observe(control, control == "CA-003" ? status : ObservationStatus.Pass);
        var completion = Completion();

        Assert.False(completion.Complete);
        var open = Assert.Single(completion.Requirements, r => r.Outstanding);
        Assert.Equal("CA-003", open.InstanceKey);
        Assert.Contains($"The current outcome is {status}.", open.Reasons);
    }

    [Fact]
    public void A_pass_with_no_stored_evidence_is_not_verified()
    {
        foreach (var control in Controls) Observe(control, evidence: control != "ID-001");
        var open = Assert.Single(Completion().Requirements, r => r.Outstanding);
        Assert.Equal("ID-001", open.InstanceKey);
    }

    [Fact]
    public void A_pass_that_needs_review_is_outstanding_and_the_job_is_blocked_by_a_changed_standard()
    {
        foreach (var control in Controls) Observe(control);
        _clock.UtcNow = _clock.UtcNow.AddDays(91);
        var overdue = Completion();
        Assert.False(overdue.Complete);
        Assert.All(overdue.Requirements, r => Assert.Contains("Outcome: " + ReviewReason.ReviewOverdue, r.Reasons));

        _clock.UtcNow = _clock.UtcNow.AddDays(-91);
        var changed = TestData.Standard();
        changed.IntegrityDigest = new string('0', 64);
        var blocked = Completion(changed);
        Assert.False(blocked.Complete);
        Assert.Contains(blocked.Blockers, b => b.Contains(ReviewReason.StandardChanged));
    }

    [Fact]
    public void An_open_decision_leaves_work_outstanding_even_with_a_pass()
    {
        foreach (var control in Controls) Observe(control);
        Decide("CA-001", DispositionDecision.Investigate);
        var open = Assert.Single(Completion().Requirements, r => r.Outstanding);
        Assert.Equal("CA-001", open.InstanceKey);
        Assert.Contains("The current decision, investigate, leaves work outstanding.", open.Reasons);
    }

    [Fact]
    public void Retention_alone_is_a_decision_not_a_verified_outcome()
    {
        foreach (var control in Controls.Skip(1)) Observe(control);
        Decide("CA-001", DispositionDecision.RetainExternalCoverage);
        var completion = Completion();
        Assert.False(completion.Complete);
        Assert.Contains("No outcome is recorded in this job.", Requirement(completion, "CA-001").Reasons);

        Observe("CA-001");
        Assert.True(Completion().Complete);
    }

    [Fact]
    public void An_approved_departure_completes_as_an_exception_and_is_never_called_verified()
    {
        foreach (var control in Controls.Skip(1)) Observe(control);
        Decide("CA-001", DispositionDecision.ApprovedDeparture, Deviation("CA-001", DeviationKind.ApprovedDeviation).Id);
        var completion = Completion();

        Assert.True(completion.Complete);
        Assert.Equal(RequirementState.Departure, Requirement(completion, "CA-001").State);
        Assert.Equal(3, completion.Verified);
        Assert.Contains("not verified", completion.Claim);
        Assert.StartsWith("Complete with approved departures", completion.Claim);
    }

    [Fact]
    public void A_departure_whose_deviation_lapses_is_outstanding()
    {
        foreach (var control in Controls.Skip(1)) Observe(control);
        Decide("CA-001", DispositionDecision.ApprovedDeparture, Deviation("CA-001", DeviationKind.ApprovedDeviation, days: 10).Id);
        _clock.UtcNow = _clock.UtcNow.AddDays(12);

        var requirement = Requirement(Completion(), "CA-001");
        Assert.True(requirement.Outstanding);
        Assert.Contains("Decision: " + ReviewReason.DeviationMissing, requirement.Reasons);
    }

    [Fact]
    public void An_in_date_not_applicable_entry_takes_a_requirement_out_of_the_count()
    {
        foreach (var control in Controls.Where(c => c != "CMP-WIN-001")) Observe(control);
        Deviation("CMP-WIN-001", DeviationKind.NotApplicable);
        var completion = Completion();

        Assert.True(completion.Complete);
        Assert.Equal(1, completion.NotApplicable);
        Assert.Equal("Complete: all 3 requirement(s) verified.", completion.Claim);
    }

    [Fact]
    public void An_open_cutover_case_keeps_its_requirement_outstanding()
    {
        foreach (var control in Controls) Observe(control);
        _workflow.Cutover(TestData.TenantA, _job.Id, _profile, _standard, new CutoverRequest
        {
            SemanticId = "CA-001", ControlId = "CA-001", Stage = CutoverStage.Review, Owner = "Client security lead", Reason = "Replace the old policy.",
            ReviewDueAt = _clock.UtcNow.AddDays(90), SnapshotId = _capture.Id, OldObjectIds = { Policy }, RecoveryLimits = "Old policy stays enabled."
        }, Actor);

        var requirement = Requirement(Completion(), "CA-001");
        Assert.True(requirement.Outstanding);
        Assert.Equal("review", requirement.Cutover);
        Assert.Contains(requirement.Reasons, r => r.EndsWith("is open at review."));
    }

    [Fact]
    public void Computing_completion_writes_nothing()
    {
        Observe("CA-001");
        var before = Directory.EnumerateFiles(_root.Paths.DataDirectory, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText);
        Completion();
        var after = Directory.EnumerateFiles(_root.Paths.DataDirectory, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Semantic_identity_follows_existing_history_then_lineage_then_the_control()
    {
        var lineage = new ReleaseLineage
        {
            Sources = { new LineageSource { Relations = { new LineageRelation { SourceControl = "PRE-001", TargetControls = { "PRE-009" }, SemanticId = "group.policy-exclusions.users" } } } }
        };
        Assert.Equal("group.policy-exclusions.users", SemanticIdentity.Resolve("PRE-009", lineage));
        Assert.Equal("CA-001", SemanticIdentity.Resolve("CA-001", lineage));
        Assert.Equal("CA-001", SemanticIdentity.Resolve("CA-001", null));
        Assert.Equal("kept", SemanticIdentity.Resolve("PRE-009", lineage, "kept"));
        lineage.Sources.Add(new LineageSource { Relations = { new LineageRelation { SourceControl = "X", TargetControls = { "PRE-009" }, SemanticId = "other" } } });
        Assert.Equal("PRE-009", SemanticIdentity.Resolve("PRE-009", lineage));
    }
}
