using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Workflow;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>INT-050 acceptance: cutover cases whose stages need recorded evidence and never authorise or perform anything.</summary>
public sealed class CutoverWorkflowTests : IDisposable
{
    private const string OldPolicy = "aaaaaaaa-1111-4111-8111-000000000003";
    private const string NewPolicy = "aaaaaaaa-1111-4111-8111-000000000004";
    private const string PilotGroup = "aaaaaaaa-2222-4222-8222-000000000001";
    private const string Actor = "engineer@test.example";
    private readonly TempRoot _root = new();
    private readonly FixedClock _clock = new();
    private readonly EvidenceStore _store;
    private readonly JobWorkflow _workflow;
    private readonly StandardCatalogue _standard = Verified(TestData.Standard());
    private readonly TenantProfile _profile = TestData.Profile();
    private readonly TenantSnapshot _before;
    private readonly TenantJob _job;

    public CutoverWorkflowTests()
    {
        _store = new EvidenceStore(_root.Paths, NullLog.Instance);
        _workflow = new JobWorkflow(_store, _clock);
        _before = Capture(old: true, created: false);
        _store.SaveSnapshot(_before);
        _job = _workflow.OpenJob(_profile, _standard, JobIntention.LegacyBackfill, "Owner Name", "", Actor, _before.Id);
    }

    public void Dispose() => _root.Dispose();

    private static StandardCatalogue Verified(StandardCatalogue standard)
    {
        standard.IntegrityDigest = CanonicalJson.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(TestData.StandardJson));
        return standard;
    }

    private TenantSnapshot Capture(bool old, bool created, string oldState = "enabled", DateTimeOffset? at = null, bool save = false)
    {
        var capture = TestData.Snapshot(_standard, capturedAt: at ?? _clock.UtcNow.AddMinutes(-5));
        if (old) capture.Collections["conditionalAccess"].Items.Add(TestData.ConditionalAccessPolicy(OldPolicy, "Legacy MFA", oldState, new[] { TestData.Emergency }));
        if (created) capture.Collections["conditionalAccess"].Items.Add(TestData.ConditionalAccessPolicy(NewPolicy, "BDIT - CA-001 - Require MFA", "enabledForReportingButNotEnforced", new[] { TestData.Emergency }));
        capture.Collections["groups"].Items.Add(new JsonObject { ["id"] = PilotGroup, ["displayName"] = "Pilot users" });
        if (save) _store.SaveSnapshot(capture);
        return capture;
    }

    private DeploymentRun SaveRun(string acceptance = WriteAcceptance.Accepted, string readback = ConfigurationVerification.Pass)
    {
        var run = new DeploymentRun
        {
            Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, StartedAt = Timestamps.Format(_clock.UtcNow), Status = RunStatus.Completed, ToolkitVersion = "test",
            Results = { new RunResult { ControlId = "CA-001", PlannedAction = nameof(PlanAction.Create), Status = ResultStatus.Completed, WriteAcceptance = acceptance, ObjectId = NewPolicy, Configuration = readback } }
        };
        _store.SaveRun(run);
        return run;
    }

    private CutoverRequest Request(CutoverRevision? head = null, string stage = CutoverStage.Review) => new()
    {
        CaseId = head?.CaseId,
        SupersedesId = head?.Id,
        SemanticId = "ca.require-mfa",
        ControlId = "CA-001",
        Stage = stage,
        Owner = "Client security lead",
        Reason = "Recorded at " + stage,
        ReviewDueAt = _clock.UtcNow.AddDays(90),
        SnapshotId = stage == CutoverStage.Review ? _before.Id : null,
        OldObjectIds = { OldPolicy },
        NewObjectIds = head?.NewObjectIds.ToList() ?? new(),
        Overlaps = "Both policies require MFA for all users during the pilot.",
        PilotGroupIds = head?.PilotGroupIds.ToList() ?? new(),
        PilotApproval = head?.PilotApproval,
        Prerequisites = head?.Prerequisites ?? new(),
        Criteria = head?.Criteria ?? new(),
        RecoveryLimits = "Turn the new policy to report-only; the old policy stays enabled until retirement.",
        Retirement = head?.Retirement,
        RetirementApproval = head?.RetirementApproval,
        ResidualDeviationIds = head?.ResidualDeviationIds.ToList() ?? new()
    };

    private CutoverRevision Record(CutoverRequest request) => _workflow.Cutover(TestData.TenantA, _job.Id, _profile, _standard, request, Actor);

    private CutoverRevision Reviewed() => Record(Request());

    private CutoverRevision Candidate()
    {
        var request = Request(Reviewed(), CutoverStage.CandidateCreated);
        request.NewObjectIds = new() { NewPolicy };
        request.RunId = SaveRun().Id;
        return Record(request);
    }

    private CutoverRevision Piloted()
    {
        var request = Request(Candidate(), CutoverStage.PilotReviewed);
        request.SnapshotId = Capture(old: true, created: true, save: true).Id;
        request.PilotGroupIds = new() { PilotGroup };
        request.PilotApproval = new CutoverApproval { ApprovedBy = "Client CISO", Reference = "CHG-1001" };
        request.Prerequisites = new() { new CutoverPrerequisite { Description = "Pilot users registered for MFA", Met = true } };
        return Record(request);
    }

    private CutoverRevision Verified()
    {
        var request = Request(Piloted(), CutoverStage.EffectivenessVerified);
        request.Criteria = new() { new CutoverCriterion { Description = "Pilot user is prompted for MFA", Result = CriterionResult.Passed, Detail = "Sign-in log for pilot user 1 shows MFA satisfied." } };
        return Record(request);
    }

    private CutoverRevision RetirementReviewed(string decision = RetirementDecision.Retire)
    {
        var request = Request(Verified(), CutoverStage.RetirementReviewed);
        request.Retirement = decision;
        request.RetirementApproval = new CutoverApproval { ApprovedBy = "Client CISO", Reference = "CHG-1002" };
        return Record(request);
    }

    private CutoverRequest Closing(CutoverRevision head, TenantSnapshot fresh)
    {
        var request = Request(head, CutoverStage.Closed);
        request.SnapshotId = fresh.Id;
        return request;
    }

    private JobProjection Project(TenantSnapshot? capture = null) =>
        JobProjection.Build(_store, TestData.TenantA, _job.Id, _standard, _profile, _clock.UtcNow, capture);

    private void Later() => _clock.UtcNow = _clock.UtcNow.AddHours(1);

    [Fact]
    public void A_case_closes_only_after_every_stage_and_a_fresh_capture_showing_the_retirement()
    {
        var mappingsBefore = ToolkitJson.Serialize(_store.LoadMappings(TestData.TenantA));
        var head = RetirementReviewed();
        Later();
        var fresh = Capture(old: false, created: true, at: _clock.UtcNow.AddMinutes(-1), save: true);
        var closed = Record(Closing(head, fresh));

        var projected = Assert.Single(Project(fresh).Cutovers);
        Assert.True(projected.Closed);
        Assert.Equal(closed.Id, projected.Current!.Id);
        Assert.Equal(CutoverStage.Order, projected.History.Select(r => r.Stage));
        Assert.All(projected.History, r => Assert.Equal(closed.CaseId, r.CaseId));
        // Nothing was adopted or mapped along the way.
        Assert.Equal(mappingsBefore, ToolkitJson.Serialize(_store.LoadMappings(TestData.TenantA)));
    }

    [Fact]
    public void The_record_carries_no_session_token_or_executable_request()
    {
        var names = typeof(CutoverRevision).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain(names, n => n.Contains("Token", StringComparison.OrdinalIgnoreCase) || n.Contains("Session", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Request", StringComparison.OrdinalIgnoreCase) || n.Contains("Execute", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Activat", StringComparison.OrdinalIgnoreCase) || n.Contains("Payload", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_case_starts_at_review_with_the_old_protection_as_captured()
    {
        Assert.Throws<SafetyViolationException>(() => Record(Request(stage: CutoverStage.CandidateCreated)));
        var noCapture = Request(); noCapture.SnapshotId = null;
        Assert.Throws<ConfigurationException>(() => Record(noCapture));
        var unknown = Request(); unknown.OldObjectIds = new() { Guid.NewGuid().ToString() };
        Assert.Throws<ConfigurationException>(() => Record(unknown));
        var none = Request(); none.OldObjectIds.Clear();
        Assert.Throws<ConfigurationException>(() => Record(none));
        Assert.Empty(_store.LoadCutovers(TestData.TenantA).Revisions);
    }

    [Fact]
    public void Stages_move_forward_one_at_a_time()
    {
        var reviewed = Reviewed();
        var skip = Request(reviewed, CutoverStage.PilotReviewed);
        Assert.Throws<SafetyViolationException>(() => Record(skip));
    }

    [Fact]
    public void A_candidate_needs_a_run_that_wrote_and_read_back_each_new_object()
    {
        var reviewed = Reviewed();
        var request = Request(reviewed, CutoverStage.CandidateCreated);
        request.NewObjectIds = new() { NewPolicy };
        Assert.Throws<ConfigurationException>(() => Record(request));
        request.RunId = SaveRun(acceptance: WriteAcceptance.Unknown).Id;
        Assert.Throws<ConfigurationException>(() => Record(request));
        request.RunId = SaveRun(readback: ConfigurationVerification.Unknown).Id;
        Assert.Throws<ConfigurationException>(() => Record(request));
        request.RunId = SaveRun().Id;
        Assert.Equal(EvidenceKind.Run, Record(request).Evidence.Single().Kind);
    }

    [Fact]
    public void A_pilot_is_a_real_approved_group_with_its_prerequisites_met()
    {
        var candidate = Candidate();
        CutoverRequest Pilot(Action<CutoverRequest> change)
        {
            var request = Request(candidate, CutoverStage.PilotReviewed);
            request.SnapshotId = Capture(old: true, created: true, save: true).Id;
            request.PilotGroupIds = new() { PilotGroup };
            request.PilotApproval = new CutoverApproval { ApprovedBy = "Client CISO", Reference = "CHG-1001" };
            request.Prerequisites = new() { new CutoverPrerequisite { Description = "MFA registration", Met = true } };
            change(request);
            return request;
        }
        Assert.Throws<ConfigurationException>(() => Record(Pilot(r => r.PilotGroupIds = new() { "All" })));
        Assert.Throws<ConfigurationException>(() => Record(Pilot(r => r.PilotGroupIds = new() { Guid.NewGuid().ToString() })));
        Assert.Throws<ConfigurationException>(() => Record(Pilot(r => r.PilotGroupIds.Clear())));
        Assert.Throws<ConfigurationException>(() => Record(Pilot(r => r.PilotApproval = null)));
        Assert.Throws<ConfigurationException>(() => Record(Pilot(r => r.Prerequisites[0].Met = false)));
        Assert.Throws<ConfigurationException>(() => Record(Pilot(r => r.SnapshotId = null)));
        Assert.Equal(CutoverStage.PilotReviewed, Record(Pilot(_ => { })).Stage);
    }

    [Theory]
    [InlineData(CriterionResult.Failed)]
    [InlineData(CriterionResult.NotRun)]
    public void Effectiveness_needs_every_criterion_actually_passed(string result)
    {
        var request = Request(Piloted(), CutoverStage.EffectivenessVerified);
        request.Criteria = new()
        {
            new CutoverCriterion { Description = "Pilot user is prompted for MFA", Result = CriterionResult.Passed, Detail = "Seen in sign-in logs." },
            new CutoverCriterion { Description = "Shared device sign-in still works", Result = result, Detail = result == CriterionResult.NotRun ? "" : "Device sign-in blocked." }
        };
        Assert.Throws<ConfigurationException>(() => Record(request));
        request.Criteria.Clear();
        Assert.Throws<ConfigurationException>(() => Record(request));
    }

    [Fact]
    public void Approval_to_retire_never_closes_the_case_by_itself()
    {
        var head = RetirementReviewed();
        Later();
        // The old policy is still there: approval is not retirement.
        var stillThere = Capture(old: true, created: true, at: _clock.UtcNow.AddMinutes(-1), save: true);
        Assert.Throws<ConfigurationException>(() => Record(Closing(head, stillThere)));
        // A capture from before the retirement review is not fresh.
        var stale = Capture(old: false, created: true, at: _clock.UtcNow.AddHours(-2), save: true);
        Assert.Throws<ConfigurationException>(() => Record(Closing(head, stale)));
        // The new protection has to be in place too.
        var nothing = Capture(old: false, created: false, at: _clock.UtcNow.AddMinutes(-1), save: true);
        Assert.Throws<ConfigurationException>(() => Record(Closing(head, nothing)));
        // An incomplete capture cannot show absence.
        var partial = Capture(old: false, created: true, at: _clock.UtcNow.AddMinutes(-1));
        partial.Complete = false;
        _store.SaveSnapshot(partial);
        Assert.Throws<ConfigurationException>(() => Record(Closing(head, partial)));

        var projected = Assert.Single(Project().Cutovers);
        Assert.False(projected.Closed);
        Assert.Equal(CutoverStage.RetirementReviewed, projected.Current!.Stage);
    }

    [Fact]
    public void Approved_coexistence_closes_with_both_protections_present()
    {
        var head = RetirementReviewed(RetirementDecision.RetainCoexistence);
        Later();
        var gone = Capture(old: false, created: true, at: _clock.UtcNow.AddMinutes(-1), save: true);
        Assert.Throws<ConfigurationException>(() => Record(Closing(head, gone)));
        var both = Capture(old: true, created: true, at: _clock.UtcNow.AddMinutes(-1), save: true);
        Record(Closing(head, both));
        Assert.True(Assert.Single(Project(both).Cutovers).Closed);
    }

    [Fact]
    public void A_closed_case_is_not_reopened()
    {
        var head = RetirementReviewed();
        Later();
        var fresh = Capture(old: false, created: true, at: _clock.UtcNow.AddMinutes(-1), save: true);
        var closed = Record(Closing(head, fresh));
        var reopen = Request(closed, CutoverStage.Review);
        reopen.SnapshotId = _before.Id;
        Assert.Throws<SafetyViolationException>(() => Record(reopen));
    }

    [Fact]
    public void An_unsupported_scenario_stops_the_case_with_an_owner_and_escalation_path()
    {
        var reviewed = Reviewed();
        var stop = Request(reviewed);
        stop.Unsupported = new CutoverEscalation { Reason = "Old policy targets a guest population the tool cannot pilot.", Owner = "Client identity team", Path = "Raise with the client architect" };
        var stopped = Record(stop);
        Assert.True(Assert.Single(Project().Cutovers).Escalated);

        var advance = Request(stopped, CutoverStage.CandidateCreated);
        advance.NewObjectIds = new() { NewPolicy };
        advance.RunId = SaveRun().Id;
        Assert.Throws<SafetyViolationException>(() => Record(advance));

        var cleared = Record(Request(stopped));
        Assert.False(Assert.Single(Project().Cutovers).Escalated);
        advance = Request(cleared, CutoverStage.CandidateCreated);
        advance.NewObjectIds = new() { NewPolicy };
        advance.RunId = SaveRun().Id;
        Assert.Equal(CutoverStage.CandidateCreated, Record(advance).Stage);
    }

    [Fact]
    public void A_revision_supersedes_the_current_one_and_keeps_its_requirement()
    {
        var first = Reviewed();
        var second = Record(Request(first));
        Assert.Throws<SafetyViolationException>(() => Record(Request(first)));
        var other = Request(second); other.ControlId = "CA-003"; other.SemanticId = "ca.block-legacy";
        Assert.Throws<SafetyViolationException>(() => Record(other));
        var moved = Request(second, CutoverStage.Review); moved.OldObjectIds = new() { TestData.Emergency };
        Assert.NotEqual(first.OldMaterialDigest, Record(moved).OldMaterialDigest);

        var candidate = Request(_store.LoadCutovers(TestData.TenantA).Revisions.Single(r => r.SupersedesId == second.Id), CutoverStage.CandidateCreated);
        candidate.OldObjectIds = new() { OldPolicy };
        candidate.NewObjectIds = new() { NewPolicy };
        candidate.RunId = SaveRun().Id;
        Assert.Throws<SafetyViolationException>(() => Record(candidate));
    }

    [Fact]
    public void A_changed_old_object_or_tampered_run_needs_review()
    {
        var candidate = Candidate();
        Assert.False(Assert.Single(Project(Capture(old: true, created: true)).Cutovers).NeedsReview);
        Assert.Contains(ReviewReason.MaterialChanged, Assert.Single(Project(Capture(old: true, created: true, oldState: "disabled")).Cutovers).ReviewReasons);

        var runFile = Directory.EnumerateFiles(Path.Combine(_store.TenantDirectory(TestData.TenantA), "runs"), "*.json").Single(f => f.Contains(candidate.Evidence.Single().Id));
        var node = ToolkitJson.ParseObject(File.ReadAllText(runFile));
        node["status"] = RunStatus.Error;
        File.WriteAllText(runFile, node.ToJsonString());
        Assert.Contains(ReviewReason.EvidenceMissing, Assert.Single(Project().Cutovers).ReviewReasons);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Later_stages_cannot_hide_missing_candidate_evidence(bool modify)
    {
        var head = RetirementReviewed();
        Later();
        var fresh = Capture(old: false, created: true, at: _clock.UtcNow.AddMinutes(-1), save: true);
        Record(Closing(head, fresh));
        Assert.True(Assert.Single(Project(fresh).Cutovers).Closed);
        var runFile = Directory.EnumerateFiles(Path.Combine(_store.TenantDirectory(TestData.TenantA), "runs"), "*.json").Single();
        if (modify)
        {
            var node = ToolkitJson.ParseObject(File.ReadAllText(runFile));
            node["status"] = RunStatus.Error;
            File.WriteAllText(runFile, node.ToJsonString());
        }
        else File.Delete(runFile);

        var projected = Assert.Single(Project(fresh).Cutovers);
        Assert.False(projected.Closed);
        Assert.Contains(ReviewReason.EvidenceMissing, projected.ReviewReasons);
    }

    [Theory]
    [InlineData("member")]
    [InlineData("tamper")]
    [InlineData("schema")]
    public void Malformed_or_modified_revisions_fail_closed(string change)
    {
        var revision = Reviewed();
        var file = Path.Combine(_store.TenantDirectory(TestData.TenantA), "cutovers", revision.Id + ".json");
        var node = ToolkitJson.ParseObject(File.ReadAllText(file));
        switch (change)
        {
            case "member": node["activateOnOpen"] = true; break;
            case "tamper": node["stage"] = CutoverStage.Closed; break;
            case "schema": node["schemaVersion"] = 2; break;
        }
        File.WriteAllText(file, node.ToJsonString());
        Assert.ThrowsAny<ToolkitException>(() => _store.LoadCutover(TestData.TenantA, revision.Id));
        var projection = Project();
        Assert.Empty(projection.Cutovers);
        Assert.Equal(revision.Id, Assert.Single(projection.Unreadable).File);
    }

    [Fact]
    public void An_interrupted_attachment_leaves_the_prior_revision_current()
    {
        var first = Reviewed();
        var orphan = ToolkitJson.Deserialize<CutoverRevision>(ToolkitJson.Serialize(first));
        orphan.Id = Guid.NewGuid().ToString(); orphan.SupersedesId = first.Id;
        _store.CreateCutover(orphan);
        var projection = Project();
        Assert.Equal(first.Id, Assert.Single(projection.Cutovers).Current!.Id);
        Assert.Equal(orphan.Id, Assert.Single(projection.UnattachedCutovers).Id);
    }

    [Fact]
    public void A_disposition_refers_only_to_a_case_for_the_same_requirement_in_its_job()
    {
        DispositionRequest Proposal(string? caseId) => new()
        {
            SemanticId = "ca.require-mfa", ControlId = "CA-001", Decision = DispositionDecision.ProposeReplacement, Owner = "Client security lead",
            Reason = "Replace the legacy policy.", ReviewDueAt = _clock.UtcNow.AddDays(90), CaseId = caseId
        };
        TenantDisposition Decide(DispositionRequest r) => _workflow.Decide(TestData.TenantA, _job.Id, _profile, _standard, r, Actor);

        Assert.Throws<ConfigurationException>(() => Decide(Proposal(Guid.NewGuid().ToString())));
        var reviewed = Reviewed();
        var other = Proposal(reviewed.CaseId); other.ControlId = "CA-003"; other.SemanticId = "ca.block-legacy";
        Assert.Throws<ConfigurationException>(() => Decide(other));
        Decide(Proposal(reviewed.CaseId));
        Assert.False(Assert.Single(Project().Dispositions).NeedsReview);

        File.Delete(Path.Combine(_store.TenantDirectory(TestData.TenantA), "cutovers", reviewed.Id + ".json"));
        Assert.Contains(ReviewReason.CaseMissing, Assert.Single(Project().Dispositions).ReviewReasons);
    }

    [Fact]
    public void Residual_exceptions_are_existing_deviations_for_the_control()
    {
        var head = RetirementReviewed();
        Later();
        var fresh = Capture(old: false, created: true, at: _clock.UtcNow.AddMinutes(-1), save: true);
        var request = Closing(head, fresh);
        request.ResidualDeviationIds = new() { Guid.NewGuid().ToString() };
        Assert.Throws<ConfigurationException>(() => Record(request));

        var deviation = new Deviation { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, ControlId = "CA-001", Reason = "Break-glass exclusion", ReviewBy = _clock.UtcNow.AddDays(30).ToString("yyyy-MM-dd") };
        _store.SaveDeviations(TestData.TenantA, new[] { deviation });
        request.ResidualDeviationIds = new() { deviation.Id };
        Record(request);
        Assert.True(Assert.Single(Project(fresh).Cutovers).Closed);
        _store.SaveDeviations(TestData.TenantA, Array.Empty<Deviation>());
        Assert.Contains(ReviewReason.DeviationMissing, Assert.Single(Project(fresh).Cutovers).ReviewReasons);
    }

    [Fact]
    public void Reading_the_projection_writes_nothing()
    {
        Candidate();
        var tenant = _store.TenantDirectory(TestData.TenantA);
        Dictionary<string, string> Fingerprint() => Directory.EnumerateFiles(tenant, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => CanonicalJson.Sha256Hex(File.ReadAllBytes(f)) + File.GetLastWriteTimeUtc(f).Ticks);
        var before = Fingerprint();
        Project(Capture(old: false, created: false));
        Assert.Equal(before, Fingerprint());
    }
}
