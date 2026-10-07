using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Workflow;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>INT-049 acceptance: persistent jobs and observations that preserve history and never confer authority.</summary>
public sealed class JobWorkflowTests : IDisposable
{
    private const string PolicyId = "aaaaaaaa-1111-4111-8111-000000000001";
    private readonly TempRoot _root = new();
    private readonly FixedClock _clock = new();
    private readonly EvidenceStore _store;
    private readonly JobWorkflow _workflow;
    private readonly StandardCatalogue _standard = Verified(TestData.Standard());
    private readonly TenantProfile _profile = TestData.Profile();
    private readonly TenantSnapshot _capture;

    public JobWorkflowTests()
    {
        _store = new EvidenceStore(_root.Paths, NullLog.Instance);
        _workflow = new JobWorkflow(_store, _clock);
        _capture = TestData.Snapshot(_standard);
        _capture.Collections["conditionalAccess"].Items.Add(TestData.ConditionalAccessPolicy(PolicyId, "Require MFA", "enabled", new[] { TestData.Emergency }));
        _store.SaveSnapshot(_capture);
    }

    public void Dispose() => _root.Dispose();

    private static StandardCatalogue Verified(StandardCatalogue standard)
    {
        standard.IntegrityDigest = CanonicalJson.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(TestData.StandardJson));
        return standard;
    }

    private TenantJob Open() => _workflow.OpenJob(_profile, _standard, JobIntention.LegacyBackfill, "Owner Name", "", "engineer@test.example", _capture.Id);

    private ObservationRequest Request(string status = ObservationStatus.Pass, string? supersedes = null, string control = "CA-001") => new()
    {
        SemanticId = "ca.require-mfa." + control,
        ControlId = control,
        Status = status,
        Reason = "Checked the policy in the capture.",
        ReviewDueAt = _clock.UtcNow.AddDays(90),
        SnapshotId = _capture.Id,
        ObservedObjectIds = control == "CA-001" ? new() { PolicyId } : new(),
        SupersedesId = supersedes
    };

    private TenantObservation Record(ObservationRequest request) =>
        _workflow.Record(TestData.TenantA, OpenedJob.Id, _profile, _standard, request, "engineer@test.example");

    private TenantJob? _job;
    private TenantJob OpenedJob => _job ??= Open();

    private JobProjection Project(TenantProfile? profile = null, StandardCatalogue? standard = null, TenantSnapshot? capture = null) =>
        JobProjection.Build(_store, TestData.TenantA, OpenedJob.Id, standard ?? _standard, profile ?? _profile, _clock.UtcNow, capture);

    private string TenantDir => _store.TenantDirectory(TestData.TenantA);

    [Fact]
    public void A_job_reopens_as_saved_and_carries_no_execution_authority()
    {
        var job = OpenedJob;
        var reopened = _store.RequireJob(TestData.TenantA, job.Id);
        Assert.Equal(ToolkitJson.Serialize(job), ToolkitJson.Serialize(reopened));
        Assert.Equal(ReviewedClientScope.Digest(_profile), reopened.ClientScopeDigest);
        Assert.Equal(_capture.IntegrityDigest, Assert.Single(reopened.Evidence).Sha256);

        // The record shape is the contract: no session, token, acknowledgement, approval or executable request.
        var names = typeof(TenantJob).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain(names, n => n.Contains("Token", StringComparison.OrdinalIgnoreCase) || n.Contains("Session", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Approv", StringComparison.OrdinalIgnoreCase) || n.Contains("Acknowledg", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Request", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Project().Subjects);
    }

    [Fact]
    public void A_job_from_another_tenant_is_refused()
    {
        var job = OpenedJob;
        var source = Path.Combine(TenantDir, "jobs", job.Id + ".json");
        var target = Path.Combine(_store.TenantDirectory(TestData.TenantB), "jobs", job.Id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);
        Assert.Throws<TenantMismatchException>(() => _store.RequireJob(TestData.TenantB, job.Id));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("member")]
    [InlineData("tamper")]
    [InlineData("id")]
    public void Malformed_or_modified_jobs_fail_closed(string change)
    {
        var job = OpenedJob;
        var file = Path.Combine(TenantDir, "jobs", job.Id + ".json");
        var node = ToolkitJson.ParseObject(File.ReadAllText(file));
        switch (change)
        {
            case "schema": node["schemaVersion"] = 2; break;
            case "member": node["executeOnOpen"] = true; break;
            case "tamper": node["owner"] = "Someone else"; break;
            case "id": node["id"] = Guid.NewGuid().ToString(); break;
        }
        File.WriteAllText(file, node.ToJsonString());
        Assert.ThrowsAny<ToolkitException>(() => _store.RequireJob(TestData.TenantA, job.Id));
        Assert.Single(_store.LoadJobs(TestData.TenantA).Unreadable);
    }

    [Fact]
    public void An_existing_record_id_is_never_replaced()
    {
        var job = OpenedJob;
        var copy = ToolkitJson.Deserialize<TenantJob>(ToolkitJson.Serialize(job));
        copy.Owner = "Replacement";
        Assert.Throws<SafetyViolationException>(() => _store.CreateJob(copy));
        Assert.Equal("Owner Name", _store.RequireJob(TestData.TenantA, job.Id).Owner);
    }

    [Fact]
    public void An_asserted_outcome_needs_an_actor_and_a_reason()
    {
        var noReason = Request(); noReason.Reason = " ";
        Assert.Throws<ConfigurationException>(() => Record(noReason));
        Assert.Throws<ConfigurationException>(() => _workflow.Record(TestData.TenantA, OpenedJob.Id, _profile, _standard, Request(), " "));
        var pending = Request(ObservationStatus.Pending); pending.Reason = "";
        Assert.Equal(ObservationStatus.Pending, Record(pending).Status);
    }

    [Fact]
    public void A_revision_supersedes_and_preserves_the_original()
    {
        var first = Record(Request(ObservationStatus.Fail));
        _clock.UtcNow = _clock.UtcNow.AddHours(1);
        var second = Record(Request(ObservationStatus.Pass, first.Id));

        var subject = Assert.Single(Project().Subjects);
        Assert.Equal(second.Id, subject.Current!.Id);
        Assert.True(subject.Accepted);
        Assert.Equal(new[] { first.Id, second.Id }, subject.History.Select(o => o.Id));
        var original = _store.LoadObservation(TestData.TenantA, first.Id)!;
        Assert.Equal(ObservationStatus.Fail, original.Status);
        Assert.Equal(first.RecordedAt, original.RecordedAt);
        Assert.Equal(first.StandardDigest, original.StandardDigest);
    }

    [Fact]
    public void Supersession_stays_one_line_within_one_requirement()
    {
        var first = Record(Request());
        // A second record for the same requirement must revise the current one, not start a parallel history.
        Assert.Throws<SafetyViolationException>(() => Record(Request()));
        // It cannot revise another requirement.
        Assert.Throws<SafetyViolationException>(() => Record(Request(ObservationStatus.Pass, first.Id, "CA-003")));
        // Nor something that does not exist.
        Assert.Throws<SafetyViolationException>(() => Record(Request(ObservationStatus.Pass, Guid.NewGuid().ToString())));
        Record(Request(ObservationStatus.Fail, first.Id));
        // And once revised, the original cannot be revised again (a fork).
        Assert.Throws<SafetyViolationException>(() => Record(Request(ObservationStatus.Pass, first.Id)));
    }

    [Fact]
    public void Changed_evidence_needs_review_and_is_never_a_fresh_pass()
    {
        var pass = Record(Request());
        var before = Fingerprint();
        var file = Directory.EnumerateFiles(Path.Combine(TenantDir, "snapshots")).Single();
        var node = ToolkitJson.ParseObject(File.ReadAllText(file));
        node["capturedBy"] = "someone-else@test.example";
        File.WriteAllText(file, node.ToJsonString());

        var subject = Assert.Single(Project().Subjects);
        Assert.Contains(ReviewReason.EvidenceMissing, subject.ReviewReasons);
        Assert.False(subject.Accepted);
        Assert.Equal(ObservationStatus.Pass, subject.Current!.Status);
        Assert.Equal(pass.IntegrityDigest, _store.LoadObservation(TestData.TenantA, pass.Id)!.IntegrityDigest);
        Assert.Equal(before.Where(f => !f.Key.Contains("snapshots")), Fingerprint().Where(f => !f.Key.Contains("snapshots")));
    }

    [Fact]
    public void A_changed_observed_object_needs_review()
    {
        Record(Request());
        var recapture = TestData.Snapshot(_standard);
        recapture.Collections["conditionalAccess"].Items.Add(TestData.ConditionalAccessPolicy(PolicyId, "Require MFA", "enabledForReportingButNotEnforced", new[] { TestData.Emergency }));
        Assert.Contains(ReviewReason.MaterialChanged, Assert.Single(Project(capture: recapture).Subjects).ReviewReasons);

        var unchanged = TestData.Snapshot(_standard);
        unchanged.Collections["conditionalAccess"].Items.Add(TestData.ConditionalAccessPolicy(PolicyId, "Require MFA", "enabled", new[] { TestData.Emergency }));
        Assert.True(Assert.Single(Project(capture: unchanged).Subjects).Accepted);

        var removed = TestData.Snapshot(_standard);
        Assert.Contains(ReviewReason.MaterialChanged, Assert.Single(Project(capture: removed).Subjects).ReviewReasons);
    }

    [Fact]
    public void Changed_client_inputs_need_review_but_a_relabel_does_not()
    {
        Record(Request());
        var relabelled = ToolkitJson.Deserialize<TenantProfile>(ToolkitJson.Serialize(_profile));
        relabelled.Company = "Renamed Ltd";
        Assert.True(Assert.Single(Project(profile: relabelled).Subjects).Accepted);
        Assert.Empty(Project(profile: relabelled).JobReviewReasons);

        var changed = ToolkitJson.Deserialize<TenantProfile>(ToolkitJson.Serialize(_profile));
        changed.Parameters.PilotGroupId = "aaaaaaaa-0000-4000-8000-0000000000fe";
        var projection = Project(profile: changed);
        Assert.Contains(ReviewReason.ClientInputsChanged, projection.JobReviewReasons);
        Assert.Contains(ReviewReason.ClientInputsChanged, Assert.Single(projection.Subjects).ReviewReasons);
    }

    [Fact]
    public void Changed_catalogue_bytes_under_the_same_release_need_review()
    {
        Record(Request());
        var changed = TestData.Standard();
        changed.IntegrityDigest = new string('0', 64);
        var projection = Project(standard: changed);
        Assert.Contains(ReviewReason.StandardChanged, projection.JobReviewReasons);
        Assert.Contains(ReviewReason.StandardChanged, Assert.Single(projection.Subjects).ReviewReasons);
    }

    [Fact]
    public void An_overdue_review_needs_review()
    {
        Record(Request());
        _clock.UtcNow = _clock.UtcNow.AddDays(91);
        Assert.Contains(ReviewReason.ReviewOverdue, Assert.Single(Project().Subjects).ReviewReasons);
    }

    [Fact]
    public void An_interrupted_attachment_leaves_the_prior_history_current()
    {
        var first = Record(Request());
        var orphan = ToolkitJson.Deserialize<TenantObservation>(ToolkitJson.Serialize(first));
        orphan.Id = Guid.NewGuid().ToString(); orphan.Status = ObservationStatus.Fail; orphan.SupersedesId = first.Id;
        _store.CreateObservation(orphan); // written, but the job was never replaced to attach it

        var projection = Project();
        Assert.Equal(first.Id, Assert.Single(projection.Subjects).Current!.Id);
        Assert.Equal(orphan.Id, Assert.Single(projection.Unattached).Id);
    }

    [Fact]
    public void A_fork_in_stored_history_is_never_an_accepted_outcome()
    {
        var first = Record(Request());
        var job = _store.RequireJob(TestData.TenantA, OpenedJob.Id);
        foreach (var status in new[] { ObservationStatus.Pass, ObservationStatus.Fail })
        {
            var branch = ToolkitJson.Deserialize<TenantObservation>(ToolkitJson.Serialize(first));
            branch.Id = Guid.NewGuid().ToString(); branch.Status = status; branch.SupersedesId = first.Id;
            _store.CreateObservation(branch);
            job.ObservationIds.Add(branch.Id);
        }
        _store.ReplaceJob(job);

        var subject = Assert.Single(Project().Subjects);
        Assert.Null(subject.Current);
        Assert.Contains(ReviewReason.Forked, subject.ReviewReasons);
        Assert.Equal(1, Project().Outstanding);
    }

    [Fact]
    public void A_job_cannot_be_rebound_or_lose_its_history()
    {
        Record(Request());
        var job = _store.RequireJob(TestData.TenantA, OpenedJob.Id);
        job.ClientScopeDigest = new string('1', 64);
        Assert.Throws<SafetyViolationException>(() => _store.ReplaceJob(job));
        job = _store.RequireJob(TestData.TenantA, OpenedJob.Id);
        job.ObservationIds.Clear();
        Assert.Throws<SafetyViolationException>(() => _store.ReplaceJob(job));
    }

    [Fact]
    public void Observed_objects_must_be_in_the_referenced_capture()
    {
        var request = Request();
        request.ObservedObjectIds = new() { Guid.NewGuid().ToString() };
        Assert.Throws<ConfigurationException>(() => Record(request));
        Assert.Empty(_store.LoadObservations(TestData.TenantA).Observations);
    }

    [Fact]
    public void Old_manual_checks_are_legacy_attestations_and_reading_them_writes_nothing()
    {
        var register = _store.LoadManualChecks(TestData.TenantA);
        register.Checks["ID-001"] = new ManualCheck { ControlId = "ID-001", Status = "Pass", Note = "Old note", RecordedAt = "2026-09-01T00:00:00Z", RecordedBy = "someone" };
        _store.SaveManualChecks(register);
        Record(Request());
        var before = Fingerprint();

        var projection = Project();
        Assert.Equal("ID-001", Assert.Single(projection.LegacyAttestations).ControlId);
        Assert.DoesNotContain(projection.Subjects, s => s.ControlId == "ID-001");
        Assert.Equal(before, Fingerprint());
    }

    [Fact]
    public void A_requirement_must_exist_for_this_client()
    {
        Assert.Throws<ConfigurationException>(() => Record(Request(control: "CA-999")));
    }

    private Dictionary<string, string> Fingerprint() => Directory.EnumerateFiles(TenantDir, "*", SearchOption.AllDirectories)
        .ToDictionary(f => f, f => CanonicalJson.Sha256Hex(File.ReadAllBytes(f)) + File.GetLastWriteTimeUtc(f).Ticks);
}
