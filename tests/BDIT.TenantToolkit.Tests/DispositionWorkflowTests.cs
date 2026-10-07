using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Workflow;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>INT-050 acceptance: legacy dispositions that record decisions without granting ownership or write rights.</summary>
public sealed class DispositionWorkflowTests : IDisposable
{
    private const string ExternalPolicy = "aaaaaaaa-1111-4111-8111-000000000002";
    private const string Actor = "engineer@test.example";
    private readonly TempRoot _root = new();
    private readonly FixedClock _clock = new();
    private readonly EvidenceStore _store;
    private readonly JobWorkflow _workflow;
    private readonly StandardCatalogue _standard = Verified(TestData.Standard());
    private readonly TenantProfile _profile = TestData.Profile();
    private readonly TenantSnapshot _capture;
    private readonly TenantJob _job;

    public DispositionWorkflowTests()
    {
        _store = new EvidenceStore(_root.Paths, NullLog.Instance);
        _workflow = new JobWorkflow(_store, _clock);
        _capture = Capture("enabled");
        _store.SaveSnapshot(_capture);
        _job = _workflow.OpenJob(_profile, _standard, JobIntention.LegacyBackfill, "Owner Name", "", Actor, _capture.Id);
    }

    public void Dispose() => _root.Dispose();

    private static StandardCatalogue Verified(StandardCatalogue standard)
    {
        standard.IntegrityDigest = CanonicalJson.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(TestData.StandardJson));
        return standard;
    }

    private TenantSnapshot Capture(string state)
    {
        var capture = TestData.Snapshot(_standard);
        // Same display name as the standard's policy: a matching name must still grant nothing.
        capture.Collections["conditionalAccess"].Items.Add(TestData.ConditionalAccessPolicy(ExternalPolicy, "Require MFA", state, new[] { TestData.Emergency }));
        return capture;
    }

    private DispositionRequest Request(string decision = DispositionDecision.RetainExternalCoverage, string? supersedes = null, string control = "CA-001",
        string? snapshotId = null, string? deviationId = null) => new()
    {
        SemanticId = "ca.require-mfa." + control,
        ControlId = control,
        Decision = decision,
        Owner = "Client security lead",
        Reason = "The client's existing policy covers this requirement.",
        ReviewDueAt = _clock.UtcNow.AddDays(90),
        SnapshotId = snapshotId ?? _capture.Id,
        ObservedObjectIds = decision == DispositionDecision.RetainExternalCoverage ? new() { ExternalPolicy } : new(),
        DeviationId = deviationId,
        SupersedesId = supersedes
    };

    private TenantDisposition Decide(DispositionRequest request) =>
        _workflow.Decide(TestData.TenantA, _job.Id, _profile, _standard, request, Actor);

    private JobProjection Project(StandardCatalogue? standard = null, TenantSnapshot? capture = null) =>
        JobProjection.Build(_store, TestData.TenantA, _job.Id, standard ?? _standard, _profile, _clock.UtcNow, capture);

    private string TenantDir => _store.TenantDirectory(TestData.TenantA);

    private Deviation SaveDeviation(string control = "CA-001", DeviationKind kind = DeviationKind.ApprovedDeviation)
    {
        var deviation = new Deviation
        {
            Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, ControlId = control, Kind = kind, Reason = "Approved by the client",
            ApprovedBy = "Client CISO", ReviewBy = _clock.UtcNow.AddDays(30).ToString("yyyy-MM-dd")
        };
        _store.SaveDeviations(TestData.TenantA, _store.LoadDeviations(TestData.TenantA).Append(deviation).ToList());
        return deviation;
    }

    [Fact]
    public void Retaining_an_external_policy_creates_no_mapping_and_writes_only_the_record_and_its_job()
    {
        var before = Fingerprint();
        var retained = Decide(Request());

        var changed = Fingerprint().Where(f => !before.TryGetValue(f.Key, out var old) || old != f.Value).Select(f => f.Key).ToList();
        Assert.Equal(2, changed.Count);
        Assert.Contains(changed, f => f.EndsWith(Path.Combine("dispositions", retained.Id + ".json")));
        Assert.Contains(changed, f => f.EndsWith(Path.Combine("jobs", _job.Id + ".json")));
        Assert.Empty(_store.LoadMappings(TestData.TenantA).ByControl);
        Assert.False(_store.LoadMappings(TestData.TenantA).IsManagedObject(ExternalPolicy));
        Assert.Empty(_store.LoadDeviations(TestData.TenantA));

        var decided = Assert.Single(Project().Dispositions);
        Assert.True(decided.Settled);
        Assert.Equal(retained.Id, decided.Current!.Id);
        // Retention is an engineer decision, not an observed pass of the requirement.
        Assert.Empty(Project().Subjects);
    }

    [Fact]
    public void Retention_names_the_exact_external_objects()
    {
        var request = Request();
        request.ObservedObjectIds.Clear();
        Assert.Throws<ConfigurationException>(() => Decide(request));
        request.ObservedObjectIds.Add(Guid.NewGuid().ToString());
        Assert.Throws<ConfigurationException>(() => Decide(request));
        Assert.Empty(_store.LoadDispositions(TestData.TenantA).Dispositions);
    }

    [Fact]
    public void A_decision_needs_an_owner_an_actor_and_a_reason()
    {
        var noOwner = Request(); noOwner.Owner = " ";
        Assert.Throws<ConfigurationException>(() => Decide(noOwner));
        var noReason = Request(DispositionDecision.Investigate); noReason.Reason = "";
        Assert.Throws<ConfigurationException>(() => Decide(noReason));
        Assert.Throws<ConfigurationException>(() => _workflow.Decide(TestData.TenantA, _job.Id, _profile, _standard, Request(), " "));
        var unknown = Request(); unknown.Decision = "Compliant";
        Assert.Throws<ConfigurationException>(() => Decide(unknown));
    }

    [Fact]
    public void An_approved_departure_relies_on_an_existing_deviation_and_never_creates_one()
    {
        Assert.Throws<ConfigurationException>(() => Decide(Request(DispositionDecision.ApprovedDeparture)));
        Assert.Throws<ConfigurationException>(() => Decide(Request(DispositionDecision.ApprovedDeparture, deviationId: Guid.NewGuid().ToString())));
        Assert.Throws<ConfigurationException>(() => Decide(Request(DispositionDecision.ApprovedDeparture, deviationId: SaveDeviation("CA-003").Id)));
        Assert.Throws<ConfigurationException>(() => Decide(Request(DispositionDecision.ApprovedDeparture, deviationId: SaveDeviation(kind: DeviationKind.NotApplicable).Id)));
        Assert.Throws<ConfigurationException>(() => Decide(Request(DispositionDecision.Investigate, deviationId: SaveDeviation().Id)));
        Assert.Empty(_store.LoadDispositions(TestData.TenantA).Dispositions);

        var deviation = SaveDeviation();
        var count = _store.LoadDeviations(TestData.TenantA).Count;
        Decide(Request(DispositionDecision.ApprovedDeparture, deviationId: deviation.Id));
        Assert.Equal(count, _store.LoadDeviations(TestData.TenantA).Count);
        Assert.True(Assert.Single(Project().Dispositions).Settled);

        // Withdrawing the deviation leaves the decision in place but no longer standing.
        _store.SaveDeviations(TestData.TenantA, _store.LoadDeviations(TestData.TenantA).Where(d => d.Id != deviation.Id).ToList());
        var decided = Assert.Single(Project().Dispositions);
        Assert.Contains(ReviewReason.DeviationMissing, decided.ReviewReasons);
        Assert.False(decided.Settled);
    }

    [Fact]
    public void An_overdue_deviation_needs_review()
    {
        Decide(Request(DispositionDecision.ApprovedDeparture, deviationId: SaveDeviation().Id));
        _clock.UtcNow = _clock.UtcNow.AddDays(31);
        Assert.Contains(ReviewReason.DeviationMissing, Assert.Single(Project().Dispositions).ReviewReasons);
    }

    [Theory]
    [InlineData(DispositionDecision.Investigate)]
    [InlineData(DispositionDecision.ManualWork)]
    [InlineData(DispositionDecision.AddMissingCandidate)]
    [InlineData(DispositionDecision.ProposeReplacement)]
    public void Work_decisions_leave_the_requirement_outstanding(string decision)
    {
        Decide(Request(decision));
        var decided = Assert.Single(Project().Dispositions);
        Assert.False(decided.NeedsReview);
        Assert.False(decided.Settled);
        Assert.Empty(_store.LoadMappings(TestData.TenantA).ByControl);
    }

    [Fact]
    public void Repeating_an_unchanged_decision_adds_no_duplicate()
    {
        var first = Decide(Request());
        Assert.Equal(first.Id, Decide(Request()).Id);
        Assert.Equal(first.Id, Decide(Request(supersedes: first.Id)).Id);

        // An unchanged recapture is still the same decision.
        var recapture = Capture("enabled");
        _store.SaveSnapshot(recapture);
        Assert.Equal(first.Id, Decide(Request(snapshotId: recapture.Id)).Id);
        Assert.Single(_store.LoadDispositions(TestData.TenantA).Dispositions);
        Assert.Single(_store.RequireJob(TestData.TenantA, _job.Id).DispositionIds!);
    }

    [Fact]
    public void A_changed_object_needs_review_and_a_revision_that_supersedes_the_original()
    {
        var first = Decide(Request());
        var changed = Capture("disabled");
        _store.SaveSnapshot(changed);
        var decided = Assert.Single(Project(capture: changed).Dispositions);
        Assert.Contains(ReviewReason.MaterialChanged, decided.ReviewReasons);
        Assert.False(decided.Settled);

        Assert.Throws<SafetyViolationException>(() => Decide(Request(snapshotId: changed.Id)));
        _clock.UtcNow = _clock.UtcNow.AddHours(1);
        var revised = Decide(Request(DispositionDecision.Investigate, first.Id, snapshotId: changed.Id));

        decided = Assert.Single(Project(capture: changed).Dispositions);
        Assert.Equal(revised.Id, decided.Current!.Id);
        Assert.Equal(new[] { first.Id, revised.Id }, decided.History.Select(d => d.Id));
        var original = _store.LoadDisposition(TestData.TenantA, first.Id)!;
        Assert.Equal(DispositionDecision.RetainExternalCoverage, original.Decision);
        Assert.Equal(first.IntegrityDigest, original.IntegrityDigest);
    }

    [Fact]
    public void Supersession_stays_one_line_within_one_requirement()
    {
        var first = Decide(Request(DispositionDecision.Investigate));
        Assert.Throws<SafetyViolationException>(() => Decide(Request(DispositionDecision.ManualWork)));
        Assert.Throws<SafetyViolationException>(() => Decide(Request(DispositionDecision.Investigate, first.Id, "CA-003")));
        Assert.Throws<SafetyViolationException>(() => Decide(Request(DispositionDecision.ManualWork, Guid.NewGuid().ToString())));
        Decide(Request(DispositionDecision.ManualWork, first.Id));
        Assert.Throws<SafetyViolationException>(() => Decide(Request(DispositionDecision.AddMissingCandidate, first.Id)));
    }

    [Fact]
    public void A_fork_in_stored_history_is_never_settled()
    {
        var first = Decide(Request());
        var job = _store.RequireJob(TestData.TenantA, _job.Id);
        foreach (var decision in new[] { DispositionDecision.RetainExternalCoverage, DispositionDecision.Investigate })
        {
            var branch = ToolkitJson.Deserialize<TenantDisposition>(ToolkitJson.Serialize(first));
            branch.Id = Guid.NewGuid().ToString(); branch.Decision = decision; branch.SupersedesId = first.Id;
            if (decision != DispositionDecision.RetainExternalCoverage) branch.ObservedObjectIds.Clear();
            if (decision != DispositionDecision.RetainExternalCoverage) branch.MaterialDigest = "";
            _store.CreateDisposition(branch);
            job.DispositionIds!.Add(branch.Id);
        }
        _store.ReplaceJob(job);

        var decided = Assert.Single(Project().Dispositions);
        Assert.Null(decided.Current);
        Assert.Contains(ReviewReason.Forked, decided.ReviewReasons);
        Assert.False(decided.Settled);
    }

    [Fact]
    public void An_interrupted_attachment_leaves_the_prior_decision_current()
    {
        var first = Decide(Request());
        var orphan = ToolkitJson.Deserialize<TenantDisposition>(ToolkitJson.Serialize(first));
        orphan.Id = Guid.NewGuid().ToString(); orphan.SupersedesId = first.Id;
        _store.CreateDisposition(orphan);

        var projection = Project();
        Assert.Equal(first.Id, Assert.Single(projection.Dispositions).Current!.Id);
        Assert.Equal(orphan.Id, Assert.Single(projection.UnattachedDispositions).Id);
    }

    [Fact]
    public void Changed_catalogue_bytes_need_review()
    {
        Decide(Request());
        var changed = TestData.Standard();
        changed.IntegrityDigest = new string('0', 64);
        Assert.Contains(ReviewReason.StandardChanged, Assert.Single(Project(standard: changed).Dispositions).ReviewReasons);
    }

    [Fact]
    public void An_overdue_decision_needs_review_and_can_be_confirmed_by_a_new_record()
    {
        var first = Decide(Request());
        _clock.UtcNow = _clock.UtcNow.AddDays(91);
        Assert.Contains(ReviewReason.ReviewOverdue, Assert.Single(Project().Dispositions).ReviewReasons);

        var confirmed = Decide(Request(supersedes: first.Id));
        Assert.NotEqual(first.Id, confirmed.Id);
        Assert.True(Assert.Single(Project().Dispositions).Settled);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("member")]
    [InlineData("tamper")]
    [InlineData("tenant")]
    public void Malformed_or_modified_dispositions_fail_closed(string change)
    {
        var disposition = Decide(Request());
        var file = Path.Combine(TenantDir, "dispositions", disposition.Id + ".json");
        var node = ToolkitJson.ParseObject(File.ReadAllText(file));
        switch (change)
        {
            case "schema": node["schemaVersion"] = 2; break;
            case "member": node["mappingCreated"] = true; break;
            case "tamper": node["decision"] = DispositionDecision.ApprovedDeparture; break;
            case "tenant": node["tenantId"] = TestData.TenantB; break;
        }
        File.WriteAllText(file, node.ToJsonString());
        Assert.ThrowsAny<ToolkitException>(() => _store.LoadDisposition(TestData.TenantA, disposition.Id));
        Assert.Single(_store.LoadDispositions(TestData.TenantA).Unreadable);
        var projection = Project();
        Assert.Empty(projection.Dispositions);
        Assert.Equal(disposition.Id, Assert.Single(projection.Unreadable).File);
    }

    [Fact]
    public void Cutover_case_references_wait_for_the_cutover_slice()
    {
        var disposition = Decide(Request(DispositionDecision.ProposeReplacement));
        var copy = ToolkitJson.Deserialize<TenantDisposition>(ToolkitJson.Serialize(disposition));
        copy.Id = Guid.NewGuid().ToString(); copy.CaseId = Guid.NewGuid().ToString();
        Assert.Throws<ConfigurationException>(() => _store.CreateDisposition(copy));
    }

    [Fact]
    public void A_job_cannot_lose_its_dispositions()
    {
        Decide(Request());
        var job = _store.RequireJob(TestData.TenantA, _job.Id);
        job.DispositionIds = null;
        Assert.Throws<SafetyViolationException>(() => _store.ReplaceJob(job));
    }

    [Fact]
    public void A_job_without_dispositions_keeps_its_original_shape()
    {
        var file = Path.Combine(TenantDir, "jobs", _job.Id + ".json");
        Assert.DoesNotContain("dispositionIds", File.ReadAllText(file));
        Assert.Null(_store.RequireJob(TestData.TenantA, _job.Id).DispositionIds);
    }

    [Fact]
    public void Reading_the_projection_writes_nothing()
    {
        Decide(Request());
        var before = Fingerprint();
        Project(capture: Capture("disabled"));
        Assert.Equal(before, Fingerprint());
    }

    private Dictionary<string, string> Fingerprint() => Directory.EnumerateFiles(TenantDir, "*", SearchOption.AllDirectories)
        .ToDictionary(f => f, f => CanonicalJson.Sha256Hex(File.ReadAllBytes(f)) + File.GetLastWriteTimeUtc(f).Ticks);
}
