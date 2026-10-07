using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>INT-051 upgrade impact: one capture under two releases, traced through verified lineage, changing nothing.</summary>
public sealed class UpgradeImpactTests : IDisposable
{
    private static readonly string Standards = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards"));
    private readonly TempRoot _root = new();
    private readonly StandardsLoader _loader;

    public UpgradeImpactTests()
    {
        foreach (var file in Directory.GetFiles(Standards, "*.json")) File.Copy(file, Path.Combine(_root.Paths.StandardsDirectory, Path.GetFileName(file)), overwrite: true);
        var lineage = Path.Combine(_root.Paths.StandardsDirectory, "lineage");
        Directory.CreateDirectory(lineage);
        foreach (var file in Directory.GetFiles(Path.Combine(Standards, "lineage"))) File.Copy(file, Path.Combine(lineage, Path.GetFileName(file)));
        _loader = new StandardsLoader(_root.Paths, NullLog.Instance);
    }

    public void Dispose() => _root.Dispose();

    private ReleaseLineage? Lineage(string target) => ReleaseLineage.Load(_root.Paths.StandardsDirectory, StandardsManifest.Load(_root.Paths.StandardsDirectory), target);

    private UpgradeImpactReport Analyse(string from, string to, ManagedObjectMappings? mappings = null)
    {
        var source = _loader.Load(from + ".json"); var target = _loader.Load(to + ".json");
        var snapshot = TestData.Snapshot(target);
        return UpgradeImpactAnalyser.Analyse(new AssessmentEngine(new FixedClock(), "test"), snapshot, source, target, Lineage(to), TestData.Profile(),
            mappings ?? new ManagedObjectMappings { TenantId = TestData.TenantA }, Array.Empty<Deviation>(), DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Release_10_to_30_traces_every_requirement_through_lineage()
    {
        var report = Analyse("2026.09.10", "2026.09.30");
        Assert.True(report.LineageRecorded);
        UpgradeImpactRow Row(string from) => report.Rows.Single(r => r.SourceControl == from);
        Assert.Equal((UpgradeImpactChange.Renamed, "PRE-005"), (Row("PRE-004").Change, Row("PRE-004").TargetControls));
        Assert.Equal((UpgradeImpactChange.Renamed, "PRE-004"), (Row("PRE-003").Change, Row("PRE-003").TargetControls));
        Assert.Equal((UpgradeImpactChange.Replaced, "PRE-008"), (Row("PRE-005").Change, Row("PRE-005").TargetControls));
        Assert.Equal(UpgradeImpactChange.Retired, Row("ENR-003").Change);
        Assert.Equal("", Row("ENR-003").TargetStatus);
        Assert.Equal(UpgradeImpactChange.Same, Row("ID-001").Change);
        Assert.DoesNotContain(report.Rows, r => r.Change == UpgradeImpactChange.Unknown);
        var added = report.Rows.Where(r => r.Change == UpgradeImpactChange.Added).Select(r => r.TargetControls).ToList();
        Assert.Contains("PRE-011", added);
        // Reused IDs are reached through lineage, so they are not reported as new.
        Assert.DoesNotContain("PRE-004", added); Assert.DoesNotContain("PRE-005", added); Assert.DoesNotContain("PRE-009", added);
        Assert.All(report.Rows, r => Assert.NotEqual("", r.Reason));
    }

    [Fact]
    public void Without_lineage_every_requirement_is_unknown_and_needs_review()
    {
        var report = Analyse("2026.09.8", "2026.09.30");
        Assert.False(report.LineageRecorded);
        Assert.All(report.Rows.Where(r => r.SourceControl.Length > 0), r => Assert.Equal(UpgradeImpactChange.Unknown, r.Change));
        Assert.Contains(report.Notes, n => n.Contains("No verified lineage", StringComparison.Ordinal));
    }

    [Fact]
    public void Comparing_a_release_with_itself_is_refused()
    {
        var release = _loader.Load("2026.09.30.json");
        Assert.Throws<ConfigurationException>(() => UpgradeImpactAnalyser.Analyse(new AssessmentEngine(new FixedClock(), "test"), TestData.Snapshot(release), release, release,
            Lineage("2026.09.30"), TestData.Profile(), new ManagedObjectMappings(), Array.Empty<Deviation>(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Analysis_changes_no_record_and_every_export_format_writes()
    {
        var mappings = new ManagedObjectMappings { TenantId = TestData.TenantA };
        mappings.ByControl["PRE-004"] = new ManagedObjectMapping { ControlId = "PRE-004", ObjectId = "aaaaaaaa-0000-4000-8000-000000000004", Collection = "groups", Release = "2026.09.10" };
        var before = ToolkitJson.Serialize(mappings);
        var report = Analyse("2026.09.10", "2026.09.30", mappings);
        Assert.Equal(before, ToolkitJson.Serialize(mappings));

        var exporter = new ReportExporter(_root.Paths, "Test");
        foreach (var format in new[] { ExportFormat.Html, ExportFormat.Markdown, ExportFormat.Json, ExportFormat.Csv, ExportFormat.Xlsx })
            Assert.True(new FileInfo(exporter.ExportUpgradeImpact(report, format)).Length > 0);
        var markdown = MarkdownReports.UpgradeImpact(report);
        Assert.Contains("PRE-004 (GRP - Pilot Devices)", markdown);
        Assert.Contains("No tenant configuration was read or changed", markdown);
    }
}
