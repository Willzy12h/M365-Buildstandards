using System.Diagnostics;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Reports;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// CLA-20261006-01: an assessment must say whether the snapshot it read is the one that was captured. Deployment already
/// refuses modified evidence; these tests hold the reporting side to the same standard.
/// </summary>
public sealed class SnapshotIntegrityAssessmentTests
{
    private static AssessmentResult Assess(TenantSnapshot snapshot)
    {
        var standard = TestData.Standard();
        return new AssessmentEngine(new FixedClock(), "test").Assess(snapshot, standard, TestData.Profile(),
            new ManagedObjectMappings { TenantId = TestData.TenantA }, Array.Empty<Deviation>(), "test");
    }

    [Fact]
    public void Intact_modified_and_unrecorded_snapshots_are_reported_distinctly_and_modified_evidence_leads_the_limitations()
    {
        var snapshot = TestData.Snapshot(TestData.Standard());
        Assert.Equal(SnapshotIntegrityState.NotRecorded, Assess(snapshot).SnapshotIntegrity);
        Assert.Contains(Assess(snapshot).Limitations, l => l.Contains("integrity not recorded", StringComparison.OrdinalIgnoreCase));

        snapshot.IntegrityDigest = EvidenceIntegrity.Compute(snapshot);
        var intact = Assess(snapshot);
        Assert.Equal(SnapshotIntegrityState.Intact, intact.SnapshotIntegrity);
        Assert.DoesNotContain(intact.Limitations, l => l.Contains("integrity", StringComparison.OrdinalIgnoreCase) || l.Contains("MODIFIED", StringComparison.Ordinal));

        snapshot.TenantName = "Changed after capture";
        var modified = Assess(snapshot);
        Assert.Equal(SnapshotIntegrityState.Modified, modified.SnapshotIntegrity);
        Assert.StartsWith("EVIDENCE MODIFIED", modified.Limitations[0], StringComparison.Ordinal);
        Assert.Contains("evidence was modified after capture", HtmlReports.Engineer(modified), StringComparison.Ordinal);
        Assert.Contains("integrity MODIFIED after capture", MarkdownReports.Engineer(modified), StringComparison.Ordinal);
        Assert.Contains("Not for issue.", HtmlReports.ClientSummary(modified, "Test"), StringComparison.Ordinal);
        Assert.DoesNotContain("Not for issue.", HtmlReports.ClientSummary(intact, "Test"), StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_reader_refuses_a_modified_snapshot_and_labels_one_without_a_digest()
    {
        using var root = new TempRoot();
        var snapshot = TestData.Snapshot(TestData.Standard());
        var file = Path.Combine(root.Root, "snapshot.json");
        File.WriteAllText(file, ToolkitJson.Serialize(snapshot));
        Assert.Equal(SnapshotIntegrityState.NotRecorded, AssessmentEngine.IntegrityOf(AssessmentContext.ReadPrimary(file)));

        snapshot.IntegrityDigest = EvidenceIntegrity.Compute(snapshot);
        File.WriteAllText(file, ToolkitJson.Serialize(snapshot));
        Assert.Equal(SnapshotIntegrityState.Intact, AssessmentEngine.IntegrityOf(AssessmentContext.ReadPrimary(file)));

        File.WriteAllText(file, File.ReadAllText(file).Replace(snapshot.TenantName, snapshot.TenantName + " (edited)", StringComparison.Ordinal));
        Assert.Throws<IntegrityException>(() => AssessmentContext.ReadPrimary(file));
    }

    [Fact]
    public async Task Actual_headless_runner_refuses_a_modified_snapshot_without_writing_a_report()
    {
        using var root = new TempRoot();
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.09.12.json"));
        root.WriteStandard("2026.09.12.json", File.ReadAllText(source)); root.WriteManifest();
        var standard = new BDIT.TenantToolkit.Engine.Standards.StandardsLoader(root.Paths, NullLog.Instance).Load("2026.09.12.json");
        var store = new EvidenceStore(root.Paths, NullLog.Instance); var profile = TestData.Profile();
        store.SaveProfiles([profile]); store.SaveMappings(new ManagedObjectMappings { TenantId = profile.TenantId });
        var snapshot = TestData.Snapshot(standard);
        var file = store.SaveSnapshot(snapshot);

        var (intactExit, intactOut, _) = await RunCli(root.Root, file);
        Assert.Equal(0, intactExit);
        Assert.Contains("integrity intact", intactOut, StringComparison.Ordinal);

        File.WriteAllText(file, File.ReadAllText(file).Replace(snapshot.TenantName, snapshot.TenantName + " (edited)", StringComparison.Ordinal));
        var reports = Directory.Exists(root.Paths.ReportsDirectory) ? Directory.GetFiles(root.Paths.ReportsDirectory).Length : 0;
        var (exit, output, error) = await RunCli(root.Root, file);
        Assert.Equal(2, exit);
        Assert.Contains("modified after capture", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Report:", output, StringComparison.Ordinal);
        Assert.Equal(reports, Directory.GetFiles(root.Paths.ReportsDirectory).Length);
    }

    private static async Task<(int Exit, string Output, string Error)> RunCli(string root, string snapshot)
    {
        var configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        var cli = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../src/BDIT.TenantToolkit.Cli/bin/{configuration}/net10.0/bdit.dll"));
        Assert.True(File.Exists(cli), "The referenced CLI project must be built for this integration check.");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { cli, "report", "--root", root, "--release", "2026.09.12.json", "--snapshot", snapshot, "--format", "json" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        return (process.ExitCode, await stdout, await stderr);
    }
}
