using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using Xunit;

namespace BuildStandard.Tests;

public sealed class AssessmentHistoryListingTests
{
    [Fact]
    public void Corrupt_and_wrong_client_records_are_reported_while_healthy_history_remains_usable()
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var healthy = new AssessmentResult { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA,
            AssessedAt = "2026-10-13T12:00:00Z" };
        var healthyFile = store.SaveAssessment(healthy);
        var directory = Path.GetDirectoryName(healthyFile)!;
        var brokenId = Guid.NewGuid().ToString();
        var broken = Path.Combine(directory, "synthetic-corrupt-" + brokenId + ".json"); File.WriteAllText(broken, "{broken");
        var wrong = Path.Combine(directory, "synthetic-other-client.json");
        File.WriteAllText(wrong, ToolkitJson.Serialize(new AssessmentResult { TenantId = TestData.TenantB }));
        var files = Directory.GetFiles(directory).ToDictionary(file => file, File.ReadAllBytes);
        var (assessments, unreadable) = store.LoadAssessmentHistory(TestData.TenantA);
        Assert.Equal(healthy.Id, Assert.Single(assessments).Result.Id);
        Assert.Equal(2, unreadable.Count);
        Assert.Contains(unreadable, r => r.File == broken && r.Problem.Contains("could not be read", StringComparison.Ordinal));
        Assert.Contains(unreadable, r => r.File == wrong && r.Problem.Contains("another client", StringComparison.Ordinal));
        foreach (var (file, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(file));
        Assert.Throws<ConfigurationException>(() => store.LoadAssessment(TestData.TenantA, brokenId));
    }

    [Fact]
    public void Empty_history_has_no_read_failure_and_legacy_listing_retains_healthy_records()
    {
        using var root = new TempRoot(); var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var (records, problems) = store.LoadAssessmentHistory(TestData.TenantA);
        Assert.Empty(records); Assert.Empty(problems);
        var record = new AssessmentResult { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA,
            AssessedAt = "2026-10-13T12:00:00Z" };
        store.SaveAssessment(record);
        Assert.Equal(record.Id, Assert.Single(store.LoadAssessments(TestData.TenantA)).Result.Id);
    }
}
