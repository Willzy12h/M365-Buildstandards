using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using Xunit;

namespace BuildStandard.Tests;

public sealed class AssessmentHistoryListingTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    public void Null_findings_are_unreadable_without_hiding_healthy_history(string findings)
    {
        using var root = new TempRoot(); var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var healthy = new AssessmentResult { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA,
            AssessedAt = "2026-10-13T12:00:00Z" };
        var healthyFile = store.SaveAssessment(healthy);
        var id = Guid.NewGuid().ToString();
        var file = Path.Combine(Path.GetDirectoryName(healthyFile)!, "synthetic-null-" + id + ".json");
        var json = $"{{\"id\":\"{id}\",\"tenantId\":\"{TestData.TenantA}\",\"findings\":{findings}}}";
        File.WriteAllText(file, json);
        var healthyBytes = File.ReadAllBytes(healthyFile);

        var history = store.LoadAssessmentHistory(TestData.TenantA);

        Assert.Equal(healthy.Id, Assert.Single(history.Assessments).Result.Id);
        Assert.Equal(file, Assert.Single(history.Unreadable).File);
        Assert.Throws<ConfigurationException>(() => store.LoadAssessment(TestData.TenantA, id));
        Assert.Equal(json, File.ReadAllText(file));
        Assert.Equal(healthyBytes, File.ReadAllBytes(healthyFile));
    }

    [Fact]
    public void A_file_blocking_the_history_directory_is_not_empty_success()
    {
        using var root = new TempRoot(); var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var directory = Path.Combine(store.TenantDirectory(TestData.TenantA), "assessments");
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        File.WriteAllText(directory, "synthetic occupied path");

        var history = store.LoadAssessmentHistory(TestData.TenantA);

        Assert.Empty(history.Assessments);
        Assert.Equal(directory, Assert.Single(history.Unreadable).File);
        Assert.Equal("synthetic occupied path", File.ReadAllText(directory));
    }

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
    public void Duplicate_json_members_are_reported_without_escaping_history_loading()
    {
        using var root = new TempRoot(); var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var id = Guid.NewGuid().ToString();
        var directory = Path.Combine(store.TenantDirectory(TestData.TenantA), "assessments");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "synthetic-duplicate-" + id + ".json");
        var json = $"{{\"id\":\"{id}\",\"tenantId\":\"{TestData.TenantA}\",\"tenantId\":\"{TestData.TenantA}\"}}";
        File.WriteAllText(file, json);
        (IReadOnlyList<StoredAssessment> Assessments, IReadOnlyList<UnreadableRecord> Unreadable) history = ([], []);
        var error = Record.Exception(() => history = store.LoadAssessmentHistory(TestData.TenantA));
        Assert.Null(error);
        Assert.Empty(history.Assessments); Assert.Single(history.Unreadable);
        Assert.Throws<ConfigurationException>(() => store.LoadAssessment(TestData.TenantA, id));
        Assert.Equal(json, File.ReadAllText(file));
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
