using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Checks;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// Review of PR #42 (CLA-20261008-01 to -04): a scoped check must tell the engineer what the full assessment tells
/// them about the selected controls, and must not lose or overstate anything about its evidence.
/// </summary>
public sealed class ScopedCheckParityTests
{
    private static readonly string Standards = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards"));

    // CLA-20261008-01

    [Fact]
    public void Scoped_check_carries_the_same_release_lineage_review_as_the_full_assessment()
    {
        using var root = new TempRoot();
        CopyStandards(root);
        var current = new StandardsLoader(root.Paths, NullLog.Instance).Load("2026.09.30.json");
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var profile = TestData.Profile();
        store.SaveProfiles([profile]);
        // PRE-004 was the pilot devices group in 2026.09.10 and is the MAM-only users group in 2026.09.30 (INT-051).
        var mappings = new ManagedObjectMappings { TenantId = profile.TenantId };
        mappings.ByControl["PRE-004"] = new ManagedObjectMapping { ControlId = "PRE-004", ObjectId = "aaaaaaaa-0000-4000-8000-000000000004", Collection = "groups", Release = "2026.09.10",
            LastApplied = new JsonObject(), LastAppliedDigest = CanonicalJson.Sha256(new JsonObject()) };
        // An unrelated record from a release without lineage: the full assessment reports it, a PRE-004 check must not.
        mappings.ByControl["CA-001"] = new ManagedObjectMapping { ControlId = "CA-001", ObjectId = "aaaaaaaa-0000-4000-8000-000000000001", Collection = "conditionalAccess", Release = "2026.09.3" };
        store.SaveMappings(mappings);
        var snapshot = TestData.Snapshot(current);

        var full = AssessmentContext.Assess(new AssessmentEngine(new FixedClock(), "test"), store, snapshot, current, profile, "test");
        var scoped = new ScopedCheckService(new FixedClock(), "test", NullLog.Instance, root.Paths.StandardsDirectory).ReviewHistorical(snapshot, current, profile,
            CheckSelection.ForControl(current, profile, "PRE-004"), store.LoadMappings(profile.TenantId), store.LoadDeviations(profile.TenantId), "test");

        var expected = full.Findings.Single(f => f.ControlId == "PRE-004");
        var actual = Assert.Single(scoped.Assessment.Findings);
        Assert.Equal((expected.Status, expected.Reason), (actual.Status, actual.Reason));
        // The renamed-requirement wording comes only from the shipped lineage, so the scoped check used it too.
        Assert.StartsWith("Release lineage: Review needed", actual.Reason);
        Assert.Contains("PRE-005 (GRP - Pilot Devices)", actual.Reason);
        var lineage = scoped.Assessment.Limitations.Where(l => l.StartsWith("Release lineage:", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(lineage);
        Assert.All(lineage, l => Assert.Contains(l, full.Limitations));
        Assert.Contains(full.Limitations, l => l.StartsWith("Release lineage:", StringComparison.Ordinal) && l.Contains("CA-001", StringComparison.Ordinal));
        Assert.DoesNotContain(lineage, l => l.Contains("CA-001", StringComparison.Ordinal));
        ScopedCheckSchema.Read(ToolkitJson.Serialize(scoped), current, profile);
    }

    [Fact]
    public void Scoped_check_without_verified_lineage_still_flags_an_earlier_release_record()
    {
        using var root = new TempRoot();
        var (catalogue, profile, source) = Inputs();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var mappings = TestData.Mappings();
        mappings.ByControl["CA-001"] = new ManagedObjectMapping { ControlId = "CA-001", ObjectId = "aaaaaaaa-0000-4000-8000-000000000001", Collection = "conditionalAccess", Release = "2026.09.3" };
        store.SaveMappings(mappings);
        var full = AssessmentContext.Assess(new AssessmentEngine(new FixedClock(), "test"), store, source, catalogue, profile, "test");
        var scoped = new ScopedCheckService(new FixedClock(), "test", NullLog.Instance, root.Paths.StandardsDirectory).ReviewHistorical(source, catalogue, profile,
            CheckSelection.ForControl(catalogue, profile, "CA-001"), store.LoadMappings(profile.TenantId), [], "test");
        Assert.StartsWith("Release lineage:", full.Findings.Single(f => f.ControlId == "CA-001").Reason);
        Assert.Equal(full.Findings.Single(f => f.ControlId == "CA-001").Reason, Assert.Single(scoped.Assessment.Findings).Reason);
    }

    // CLA-20261008-02

    [Fact]
    public void Historical_review_uses_separate_exchange_evidence_as_the_full_assessment_does()
    {
        var (standard, profile, graph, exchange, clock) = ExchangeInputs();
        var full = new AssessmentEngine(clock, "test").Assess(graph, standard, profile, TestData.Mappings(), [], "test", exchange, CapturedAt(graph));
        var selection = CheckSelection.ForArea(standard, profile, "Exchange");
        var service = new ScopedCheckService(clock, "test", NullLog.Instance);

        var scoped = service.ReviewHistorical(graph, standard, profile, selection, TestData.Mappings(), [], "test", exchange);
        Assert.NotEmpty(scoped.Assessment.Findings);
        foreach (var finding in scoped.Assessment.Findings)
        {
            var expected = full.Findings.Single(f => f.ControlId == finding.ControlId);
            Assert.Equal((expected.Status, expected.Reason), (finding.Status, finding.Reason));
        }
        Assert.Contains(scoped.Assessment.Findings, f => f.ControlId == "EX-004" && f.Status == FindingStatus.RequiresManualReview);
        Assert.Equal(exchange.Id, scoped.SeparateExchange!.Id);
        Assert.Equal(exchange.Id, scoped.Capture.ExchangeCapture!.Id);
        Assert.Contains("separate Exchange/Purview capture " + exchange.Id, scoped.Assessment.Limitations[0], StringComparison.Ordinal);
        Assert.Null(graph.ExchangeCapture);
        Assert.Equal(scoped.Id, ScopedCheckSchema.Read(ToolkitJson.Serialize(scoped), standard, profile).Id);

        // Without it, the same check cannot assess Exchange: the separate evidence is what made the difference.
        var withoutExchange = service.ReviewHistorical(graph, standard, profile, selection, TestData.Mappings(), [], "test");
        Assert.Contains(withoutExchange.Assessment.Findings, f => f.ControlId == "EX-004" && f.Status == FindingStatus.UnableToAssess);
        Assert.Null(withoutExchange.SeparateExchange);
    }

    [Fact]
    public void Separate_exchange_evidence_is_not_carried_into_an_unrelated_check()
    {
        var (standard, profile, graph, exchange, clock) = ExchangeInputs();
        var record = new ScopedCheckService(clock, "test", NullLog.Instance).ReviewHistorical(graph, standard, profile,
            CheckSelection.ForControl(standard, profile, "CA-001"), TestData.Mappings(), [], "test", exchange);
        Assert.Null(record.SeparateExchange);
        Assert.Null(record.Capture.ExchangeCapture);
    }

    [Fact]
    public void Separate_exchange_evidence_from_another_tenant_is_refused()
    {
        var (standard, profile, graph, exchange, clock) = ExchangeInputs();
        exchange.TenantId = TestData.TenantB;
        Assert.Throws<TenantMismatchException>(() => new ScopedCheckService(clock, "test", NullLog.Instance).ReviewHistorical(graph, standard, profile,
            CheckSelection.ForArea(standard, profile, "Exchange"), TestData.Mappings(), [], "test", exchange));
    }

    [Theory]
    [InlineData("changed-carried-capture")]
    [InlineData("claimed-for-unrelated-selection")]
    [InlineData("claimed-for-live-read")]
    public void Recorded_separate_exchange_evidence_must_match_what_the_record_carries(string defect)
    {
        var (standard, profile, graph, exchange, clock) = ExchangeInputs();
        var service = new ScopedCheckService(clock, "test", NullLog.Instance);
        var record = service.ReviewHistorical(graph, standard, profile, CheckSelection.ForArea(standard, profile, "Exchange"), TestData.Mappings(), [], "test", exchange);
        var recorded = record.SeparateExchange!;
        switch (defect)
        {
            case "changed-carried-capture":
                record.Capture.ExchangeCapture!.ModuleVersion = "3.9.3";
                break;
            case "claimed-for-unrelated-selection":
                record = service.ReviewHistorical(graph, standard, profile, CheckSelection.ForControl(standard, profile, "CA-001"), TestData.Mappings(), [], "test", exchange);
                record.SeparateExchange = recorded;
                break;
            case "claimed-for-live-read":
                record.SourceMode = "liveScoped"; record.SourceCapture = null; record.AccountObjectId = TestData.Operator;
                record.Assessment.SnapshotIntegrity = SnapshotIntegrityState.Intact;
                break;
        }
        // Every digest is recomputed, so only the separate-evidence rules can refuse it.
        record.Capture.IntegrityDigest = EvidenceIntegrity.Compute(record.Capture);
        record.IntegrityDigest = EvidenceIntegrity.Compute(record);
        Assert.Throws<ConfigurationException>(() => ScopedCheckSchema.Read(ToolkitJson.Serialize(record), standard, profile));
    }

    // CLA-20261008-03

    [Fact]
    public void A_valid_result_too_large_to_store_is_returned_unsaved_with_the_reason()
    {
        using var root = new TempRoot();
        var (catalogue, profile, source) = Inputs();
        var policies = source.Collections["conditionalAccess"];
        var padding = new string('x', 1000);
        for (var i = 0; i < 9000; i++) policies.Items.Add(new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["displayName"] = "Synthetic policy " + i, ["padding"] = padding });
        policies.Count = policies.Items.Count;
        var record = Service().ReviewHistorical(source, catalogue, profile, CheckSelection.ForControl(catalogue, profile, "CA-001"), TestData.Mappings(), [], "test");
        var store = new ScopedCheckStore(root.Paths);

        var (file, reason) = store.TrySave(record, catalogue, profile);
        Assert.Null(file);
        Assert.Contains("was not saved", reason);
        Assert.Contains("8 MiB", reason);
        Assert.False(Directory.Exists(Path.Combine(root.Paths.TenantDirectory(profile.TenantId), "scoped-checks")));
        // The ordinary save still refuses it: the limit is not relaxed, the result is only not lost.
        Assert.Throws<ConfigurationException>(() => store.Save(record, catalogue, profile));
    }

    [Fact]
    public void Only_size_is_reported_as_not_saved_and_an_invalid_result_is_still_refused()
    {
        using var root = new TempRoot();
        var (catalogue, profile, source) = Inputs();
        var record = Service().ReviewHistorical(source, catalogue, profile, CheckSelection.ForControl(catalogue, profile, "CA-001"), TestData.Mappings(), [], "test");
        var store = new ScopedCheckStore(root.Paths);
        var (file, reason) = store.TrySave(record, catalogue, profile);
        Assert.Null(reason);
        Assert.Equal(record.Id, store.Read(file!, catalogue, profile).Id);

        var invalid = Service().ReviewHistorical(source, catalogue, profile, CheckSelection.ForControl(catalogue, profile, "CA-001"), TestData.Mappings(), [], "test");
        invalid.Assessment.Findings[0].Reason = "Changed";
        Assert.Throws<ConfigurationException>(() => store.TrySave(invalid, catalogue, profile));
    }

    // CLA-20261008-04

    [Fact]
    public void Historical_review_reports_the_source_integrity_not_the_derived_digest()
    {
        var (catalogue, profile, source) = Inputs(); // No integrity digest recorded.
        Assert.Equal(SnapshotIntegrityState.NotRecorded, AssessmentEngine.IntegrityOf(source));
        var record = Service().ReviewHistorical(source, catalogue, profile, CheckSelection.ForControl(catalogue, profile, "CA-001"), TestData.Mappings(), [], "test");
        Assert.Equal(SnapshotIntegrityState.NotRecorded, record.Assessment.SnapshotIntegrity);
        Assert.Contains(AssessmentEngine.IntegrityNotRecordedLimitation, record.Assessment.Limitations);
        var full = new AssessmentEngine(new FixedClock(), "test").Assess(source, catalogue, profile, TestData.Mappings(), [], "test");
        Assert.Contains(AssessmentEngine.IntegrityNotRecordedLimitation, full.Limitations);
        Assert.Equal(SnapshotIntegrityState.NotRecorded, ScopedCheckSchema.Read(ToolkitJson.Serialize(record), catalogue, profile).Assessment.SnapshotIntegrity);

        source.IntegrityDigest = EvidenceIntegrity.Compute(source);
        var intact = Service().ReviewHistorical(source, catalogue, profile, CheckSelection.ForControl(catalogue, profile, "CA-001"), TestData.Mappings(), [], "test");
        Assert.Equal(SnapshotIntegrityState.Intact, intact.Assessment.SnapshotIntegrity);
        Assert.DoesNotContain(AssessmentEngine.IntegrityNotRecordedLimitation, intact.Assessment.Limitations);
    }

    [Fact]
    public void A_historical_record_cannot_claim_better_integrity_than_a_live_read_or_an_unknown_state()
    {
        var (catalogue, profile, source) = Inputs();
        var record = Service().ReviewHistorical(source, catalogue, profile, CheckSelection.ForControl(catalogue, profile, "CA-001"), TestData.Mappings(), [], "test");
        record.Assessment.SnapshotIntegrity = SnapshotIntegrityState.Modified;
        record.IntegrityDigest = EvidenceIntegrity.Compute(record);
        Assert.Throws<ConfigurationException>(() => ScopedCheckSchema.Read(ToolkitJson.Serialize(record), catalogue, profile));
    }

    private static (StandardCatalogue, TenantProfile, TenantSnapshot) Inputs()
    {
        var catalogue = TestData.Standard(); catalogue.IntegrityDigest = new string('a', 64); // Synthetic manifest identity, not publication proof.
        return (catalogue, TestData.Profile(), TestData.Snapshot(catalogue));
    }

    private static (StandardCatalogue, TenantProfile, TenantSnapshot, ExchangeCapture, FixedClock) ExchangeInputs()
    {
        var standard = ExchangeTestData.Standard(); standard.IntegrityDigest = new string('a', 64);
        var graph = TestData.Snapshot(standard, capturedAt: ExchangeTestData.Now);
        return (standard, TestData.Profile(), graph, ExchangeTestData.Capture(), new FixedClock { UtcNow = ExchangeTestData.Now.AddMinutes(1) });
    }

    private static DateTimeOffset CapturedAt(TenantSnapshot snapshot) => Timestamps.TryParse(snapshot.CapturedAt, out var at) ? at : throw new InvalidOperationException();
    private static ScopedCheckService Service() => new(new FixedClock(), "test", NullLog.Instance);

    private static void CopyStandards(TempRoot root)
    {
        foreach (var file in Directory.GetFiles(Standards, "*.json")) File.Copy(file, Path.Combine(root.Paths.StandardsDirectory, Path.GetFileName(file)), overwrite: true);
        var lineage = Path.Combine(root.Paths.StandardsDirectory, "lineage");
        Directory.CreateDirectory(lineage);
        foreach (var file in Directory.GetFiles(Path.Combine(Standards, "lineage"))) File.Copy(file, Path.Combine(lineage, Path.GetFileName(file)));
    }
}
