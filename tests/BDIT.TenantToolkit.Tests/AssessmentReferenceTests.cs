using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Workflow;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>INT-049 assessment references: an outcome may cite a stored assessment of its capture, pinned by digest.</summary>
public sealed class AssessmentReferenceTests : IDisposable
{
    private const string Policy = "aaaaaaaa-1111-4111-8111-000000000009";
    private const string Actor = "engineer@test.example";
    private readonly TempRoot _root = new();
    private readonly FixedClock _clock = new();
    private readonly EvidenceStore _store;
    private readonly JobWorkflow _workflow;
    private readonly StandardCatalogue _standard = TestData.Standard();
    private readonly TenantProfile _profile = TestData.Profile();
    private readonly TenantSnapshot _capture;
    private readonly TenantJob _job;

    public AssessmentReferenceTests()
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

    /// <summary>Assesses the capture and stores the result, with CA-001's finding set as given.</summary>
    private AssessmentResult Assessment(FindingStatus ca001 = FindingStatus.Compliant, TenantSnapshot? capture = null, StandardCatalogue? standard = null)
    {
        var result = new AssessmentEngine(_clock, "test").Assess(capture ?? _capture, standard ?? _standard, _profile, TestData.Mappings(), [], Actor);
        result.Findings.Single(f => f.ControlId == "CA-001").Status = ca001;
        _store.SaveAssessment(result);
        return result;
    }

    private TenantObservation Observe(string assessmentId, string status = ObservationStatus.Pass, string? snapshotId = null) =>
        _workflow.Record(TestData.TenantA, _job.Id, _profile, _standard, new ObservationRequest
        {
            SemanticId = "CA-001", ControlId = "CA-001", Status = status, Reason = "Checked against the assessment.",
            ReviewDueAt = _clock.UtcNow.AddDays(90), SnapshotId = snapshotId, AssessmentId = assessmentId
        }, Actor);

    private RequirementCompletion Requirement()
    {
        var projection = JobProjection.Build(_store, TestData.TenantA, _job.Id, _standard, _profile, _clock.UtcNow, _capture);
        return JobCompletion.Build(_store, projection, _standard, _profile, _clock.UtcNow).Requirements.Single(r => r.InstanceKey == "CA-001");
    }

    [Fact]
    public void A_pass_citing_an_assessment_pins_it_and_its_capture_and_verifies()
    {
        var assessment = Assessment();
        var observation = Observe(assessment.Id);

        Assert.Equal(new[] { EvidenceKind.Snapshot, EvidenceKind.Assessment }, observation.Evidence.Select(e => e.Kind));
        Assert.Equal(_capture.Id, observation.Evidence[0].Id, ignoreCase: true);
        Assert.Equal(_store.LoadAssessment(TestData.TenantA, assessment.Id)!.Sha256, observation.Evidence[1].Sha256);
        Assert.Equal(RequirementState.Verified, Requirement().State);
    }

    [Theory]
    [InlineData(FindingStatus.Missing)]
    [InlineData(FindingStatus.UnableToAssess)]
    [InlineData(FindingStatus.SettingsMatchNotEnforced)]
    [InlineData(FindingStatus.CompliantWithDeviation)]
    public void A_pass_cannot_cite_an_assessment_that_found_the_requirement_unmet_but_a_fail_can(FindingStatus finding)
    {
        var assessment = Assessment(finding);

        Assert.Throws<SafetyViolationException>(() => Observe(assessment.Id));
        Assert.Empty(_store.LoadObservations(TestData.TenantA).Observations);

        Observe(assessment.Id, ObservationStatus.Fail);
        Assert.Single(_store.LoadObservations(TestData.TenantA).Observations);
    }

    [Fact]
    public void An_assessment_of_another_capture_or_under_another_standard_is_refused()
    {
        var other = TestData.Snapshot(_standard);
        _store.SaveSnapshot(other);
        var ofOther = Assessment(capture: other);
        Assert.Throws<ConfigurationException>(() => Observe(ofOther.Id, snapshotId: _capture.Id));

        var changed = TestData.Standard();
        changed.IntegrityDigest = new string('0', 64);
        var underOther = Assessment(standard: changed);
        Assert.Throws<ConfigurationException>(() => Observe(underOther.Id));

        Assert.Throws<ConfigurationException>(() => Observe(Guid.NewGuid().ToString()));
        Assert.Empty(_store.LoadObservations(TestData.TenantA).Observations);
    }

    [Fact]
    public void Changing_the_stored_assessment_sends_the_outcome_to_review()
    {
        var assessment = Assessment();
        Observe(assessment.Id);
        var file = Directory.EnumerateFiles(Path.Combine(_store.TenantDirectory(TestData.TenantA), "assessments")).Single();
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"Compliant\"", "\"Missing\""));

        var requirement = Requirement();
        Assert.Equal(RequirementState.Outstanding, requirement.State);
        Assert.Contains("Outcome: " + ReviewReason.EvidenceMissing, requirement.Reasons);
    }

    [Fact]
    public void Dispositions_still_rely_on_captures_only()
    {
        var disposition = _workflow.Decide(TestData.TenantA, _job.Id, _profile, _standard, new DispositionRequest
        {
            SemanticId = "CA-001", ControlId = "CA-001", Decision = DispositionDecision.Investigate, Owner = "Client security lead", Reason = "Checking.",
            ReviewDueAt = _clock.UtcNow.AddDays(90), SnapshotId = _capture.Id
        }, Actor);
        disposition.Evidence.Add(new EvidenceReference { Kind = EvidenceKind.Assessment, Id = Guid.NewGuid().ToString(), Sha256 = new string('a', 64) });

        Assert.Throws<ConfigurationException>(() => WorkflowRecordRules.ValidateDisposition(disposition));
    }
}
