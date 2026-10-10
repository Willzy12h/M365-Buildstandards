using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Checks;
using BDIT.TenantToolkit.Engine.Evidence;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public class ScopedEvidenceTests
{
    [Fact]
    public void Ordinary_snapshot_reader_preserves_supported_comments_and_trailing_commas()
    {
        var (_, _, source) = Inputs();
        var json = ToolkitJson.Serialize(source);
        json = json.Insert(json.LastIndexOf('}'), ", // historical export comment\n");
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(file, json);
            Assert.Equal(source.Id, AssessmentContext.ReadPrimary(file).Id);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Historical_filter_preserves_original_bytes_and_records_unknown_account_without_freshness_claim()
    {
        var (catalogue, profile, source) = Inputs();
        var before = ToolkitJson.Serialize(source);
        var record = Service().ReviewHistorical(source, catalogue, profile, CheckSelection.ForControl(catalogue, profile, "CA-001"), TestData.Mappings(), [], "synthetic reviewer");
        Assert.Equal(before, ToolkitJson.Serialize(source));
        Assert.Equal("historicalFiltered", record.SourceMode);
        Assert.Null(record.AccountObjectId);
        Assert.Equal(source.Id, record.SourceCapture!.Id);
        Assert.Equal(EvidenceIntegrity.Compute(source), record.SourceCapture.Sha256);
        Assert.NotEqual(source.Id, record.Capture.Id);
        Assert.Equal(source.CapturedAt, record.Capture.CapturedAt);
        Assert.False(record.Capture.Complete);
        Assert.False(record.Assessment.SnapshotComplete);
        Assert.Contains(record.Assessment.Limitations, l => l.Contains("original integrity " + SnapshotIntegrityState.NotRecorded, StringComparison.Ordinal));
        var json = ToolkitJson.Serialize(record);
        Assert.Contains("\"accountObjectId\": null", json);
        Assert.Equal(record.Id, ScopedCheckSchema.Read(json, catalogue, profile).Id);
    }

    [Fact]
    public void Missing_source_dependency_stays_explicitly_unknown()
    {
        var (catalogue, profile, source) = Inputs(); source.Collections.Remove("licences");
        var record = Service().ReviewHistorical(source, catalogue, profile, CheckSelection.ForControl(catalogue, profile, "CA-001"), TestData.Mappings(), [], "synthetic reviewer");
        Assert.Equal(CaptureStatus.NotAttempted, record.Capture.Collections["licences"].Status);
        Assert.False(record.Capture.Collections["licences"].Usable);
        Assert.Equal(FindingStatus.UnableToAssess, Assert.Single(record.Assessment.Findings).Status);
    }

    [Fact]
    public void Modified_source_cannot_be_laundered_into_new_hashed_evidence()
    {
        var (catalogue, profile, source) = Inputs();
        source.IntegrityDigest = EvidenceIntegrity.Compute(source); source.TenantName = "Changed after hashing";
        Assert.Throws<ConfigurationException>(() => Service().ReviewHistorical(source, catalogue, profile,
            CheckSelection.ForControl(catalogue, profile, "CA-001"), TestData.Mappings(), [], "synthetic reviewer"));
    }

    [Fact]
    public void Store_is_separate_immutable_and_cannot_load_as_an_ordinary_snapshot()
    {
        using var root = new TempRoot();
        var (catalogue, profile, source) = Inputs();
        var record = Historical(catalogue, profile, source);
        var store = new ScopedCheckStore(root.Paths);
        var file = store.Save(record, catalogue, profile);
        Assert.Contains("scoped-checks", file);
        var original = File.ReadAllBytes(file);
        Assert.Equal(record.Id, store.Read(file, catalogue, profile).Id);
        Assert.Throws<IOException>(() => store.Save(record, catalogue, profile));
        Assert.Equal(original, File.ReadAllBytes(file));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.tmp"));
        Assert.Throws<ConfigurationException>(() => AssessmentContext.ReadPrimary(file));
        Assert.Throws<ConfigurationException>(() => new EvidenceStore(root.Paths, NullLog.Instance).ReadJson<TenantSnapshot>(file));
        Assert.NotNull(SnapshotRequirements.IncompleteReason(record.Capture, catalogue));
    }

    [Theory]
    [InlineData("future-schema")]
    [InlineData("unknown-field")]
    [InlineData("missing-account-field")]
    [InlineData("null-collections")]
    [InlineData("null-control-list")]
    [InlineData("numeric-status")]
    [InlineData("unknown-mode")]
    [InlineData("null-recorded-time")]
    public void Strict_reader_refuses_unsupported_malformed_or_missing_members(string defect)
    {
        var (catalogue, profile, source) = Inputs();
        var node = (JsonObject)ToolkitJson.ToNode(Historical(catalogue, profile, source))!;
        switch (defect)
        {
            case "future-schema": node["schemaVersion"] = 2; break;
            case "unknown-field": node["unexpected"] = true; break;
            case "missing-account-field": node.Remove("accountObjectId"); break;
            case "null-collections": node["capture"]!["collections"] = null; break;
            case "null-control-list": node["controlIds"] = null; break;
            case "numeric-status": node["assessment"]!["findings"]![0]!["status"] = 1; break;
            case "unknown-mode": node["sourceMode"] = "fresh"; break;
            case "null-recorded-time": node["recordedAt"] = null; break;
        }
        Assert.Throws<ConfigurationException>(() => ScopedCheckSchema.Read(node.ToJsonString(), catalogue, profile));
    }

    [Fact]
    public void Duplicate_members_and_modified_content_are_refused()
    {
        var (catalogue, profile, source) = Inputs(); var record = Historical(catalogue, profile, source);
        var json = ToolkitJson.Serialize(record);
        var duplicate = json.Insert(json.IndexOf('{') + 1, "\"schemaVersion\":1,");
        Assert.Throws<ConfigurationException>(() => ScopedCheckSchema.Read(duplicate, catalogue, profile));
        record.Assessment.Findings[0].Reason = "Changed";
        Assert.Throws<ConfigurationException>(() => ScopedCheckSchema.Read(ToolkitJson.Serialize(record), catalogue, profile));
    }

    [Fact]
    public void Recomputed_digests_cannot_authorise_full_evidence_or_unrelated_dependencies()
    {
        var (catalogue, profile, source) = Inputs(); var record = Historical(catalogue, profile, source);
        record.Capture.Complete = true; record.Capture.IntegrityDigest = EvidenceIntegrity.Compute(record.Capture);
        record.IntegrityDigest = EvidenceIntegrity.Compute(record);
        Assert.Throws<ConfigurationException>(() => ScopedCheckSchema.Read(ToolkitJson.Serialize(record), catalogue, profile));
        record = Historical(catalogue, profile, source); record.CollectionKeys.Add("users");
        record.IntegrityDigest = EvidenceIntegrity.Compute(record);
        Assert.Throws<ConfigurationException>(() => ScopedCheckSchema.Read(ToolkitJson.Serialize(record), catalogue, profile));
    }

    [Fact]
    public async Task Live_read_has_verified_identity_partial_capture_and_no_historical_source()
    {
        var (catalogue, profile, _) = Inputs(); var graph = new FakeGraphClient(catalogue);
        var session = TestData.Session();
        var record = await Service().CollectAsync(graph, session, catalogue, profile, CheckSelection.ForControl(catalogue, profile, "CA-001"), TestData.Mappings(), [], null, CancellationToken.None);
        Assert.Equal("liveScoped", record.SourceMode); Assert.Equal(session.AccountObjectId, record.AccountObjectId);
        Assert.Null(record.SourceCapture); Assert.False(record.Capture.Complete); Assert.Empty(graph.Writes);
        ScopedCheckSchema.Read(ToolkitJson.Serialize(record), catalogue, profile);
    }

    [Fact]
    public async Task Identity_change_during_read_refuses_result_instead_of_rebinding_it()
    {
        var (catalogue, profile, _) = Inputs(); var session = TestData.Session();
        var graph = new FakeGraphClient(catalogue) { BeforeRead = (_, _) => { session.AccountObjectId = TestData.Emergency; return Task.CompletedTask; } };
        await Assert.ThrowsAsync<TenantMismatchException>(() => Service().CollectAsync(graph, session, catalogue, profile,
            CheckSelection.ForControl(catalogue, profile, "CA-001"), TestData.Mappings(), [], null, CancellationToken.None));
        Assert.Empty(graph.Writes);
    }

    private static (StandardCatalogue, TenantProfile, TenantSnapshot) Inputs()
    {
        var catalogue = TestData.Standard(); catalogue.IntegrityDigest = new string('a', 64); // Synthetic manifest identity, not publication proof.
        return (catalogue, TestData.Profile(), TestData.Snapshot(catalogue));
    }
    private static ScopedCheckService Service() => new(new FixedClock(), "test", NullLog.Instance);
    private static ScopedCheckEvidence Historical(StandardCatalogue catalogue, TenantProfile profile, TenantSnapshot source)
        => Service().ReviewHistorical(source, catalogue, profile, CheckSelection.ForControl(catalogue, profile, "CA-001"), TestData.Mappings(), [], "synthetic reviewer");
}
