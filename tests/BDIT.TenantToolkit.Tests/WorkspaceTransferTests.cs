using System.IO.Compression;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class WorkspaceTransferTests
{
    [Fact]
    public void Create_and_restore_share_the_exact_total_boundary_including_generated_metadata()
    {
        using var root = new TempRoot(); File.WriteAllText(root.Paths.ProfilesFile, "[]");
        var initial = new WorkspaceBackup(root.Paths).Create(); long size;
        using (var archive = ZipFile.OpenRead(initial)) size = archive.Entries.Sum(e => e.Length);
        var bounded = new WorkspaceBackup(root.Paths, size);
        var zip = bounded.Create();
        Assert.True(Directory.Exists(bounded.RestoreSeparate(zip, Path.Combine(root.Root, "exact-limit"))));
        Assert.Throws<ConfigurationException>(() => new WorkspaceBackup(root.Paths, size - 1).Create());
        Assert.Throws<ConfigurationException>(() => new WorkspaceBackup(root.Paths, size - 1).RestoreSeparate(zip, Path.Combine(root.Root, "too-large")));
        Assert.False(Directory.Exists(Path.Combine(root.Root, "too-large")));
    }
    [Fact]
    public void A_real_restored_unknown_write_still_blocks_a_fresh_plan_and_the_old_attempt_cannot_be_replayed()
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var oldPlanId = Guid.NewGuid().ToString();
        var run = new DeploymentRun { Id = Guid.NewGuid().ToString(), PlanId = oldPlanId, TenantId = TestData.TenantA,
            StartedAt = Timestamps.Format(DateTimeOffset.UtcNow), Status = RunStatus.ReviewRequired,
            Results = [new RunResult { ControlId = "CA-001", PlannedAction = nameof(PlanAction.Create), Status = ResultStatus.Error,
                WriteAcceptance = WriteAcceptance.Unknown, Configuration = ConfigurationVerification.Unknown }] };
        store.SaveRun(run);
        var backup = new WorkspaceBackup(root.Paths);
        var destination = backup.RestoreSeparate(backup.Create(), Path.Combine(root.Root, "second-installation"));
        Directory.CreateDirectory(Path.Combine(destination, "standards"));
        var restoredPaths = ToolkitPaths.Resolve(destination); restoredPaths.EnsureWritableFolders();
        var restoredStore = new EvidenceStore(restoredPaths, NullLog.Instance);
        var restoredRun = restoredStore.RequireIntactRuns(TestData.TenantA).Single();
        Assert.Equal(run.IntegrityDigest, restoredRun.IntegrityDigest);
        Assert.Equal(WriteAcceptance.Unknown, restoredRun.Results.Single().WriteAcceptance);
        var plan = new DeploymentPlan { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA,
            Rows = [new PlanRow { ControlId = "CA-001", Action = PlanAction.Create }] };
        Assert.Contains(run.Id, Assert.Throws<PlanValidationException>(() => restoredStore.AssertPlanHasNotRun(plan)).Message);
        plan.Id = oldPlanId;
        Assert.Contains("already has a deployment attempt", Assert.Throws<PlanValidationException>(() => restoredStore.AssertPlanHasNotRun(plan)).Message);
    }
    [Fact]
    public void Support_preview_and_archive_are_allowlisted_and_do_not_read_or_export_private_workspace_files()
    {
        using var root = new TempRoot();
        var secret = "SYNTHETIC-PRIVATE-DO-NOT-EXPORT";
        foreach (var folder in new[] { root.Paths.ConfigDirectory, root.Paths.DataDirectory, root.Paths.LogsDirectory, root.Paths.ReportsDirectory })
            File.WriteAllText(Path.Combine(folder, "private.json"), secret);
        var standard = TestData.Standard(); standard.Release = secret; standard.IntegrityDigest = secret;
        var preview = SupportBundle.Preview(standard);
        Assert.DoesNotContain(secret, preview); Assert.DoesNotContain(root.Root, preview);
        var zip = SupportBundle.Export(root.Paths, standard);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(new[] { "README.txt", "SHA256SUMS.txt", "technical-metadata.txt" }, archive.Entries.Select(e => e.FullName).Order());
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            var text = reader.ReadToEnd(); Assert.DoesNotContain(secret, text); Assert.DoesNotContain(root.Root, text);
            if (entry.FullName == "technical-metadata.txt") Assert.Contains(text, preview);
        }
    }

    [Fact]
    public void Backup_and_separate_restore_keep_all_evidence_bytes_unknown_fields_and_unresolved_blockers_without_authentication()
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        store.SaveProfiles([TestData.Profile()]);
        var tenant = root.Paths.TenantDirectory(TestData.TenantA);
        Directory.CreateDirectory(Path.Combine(tenant, "runs"));
        Directory.CreateDirectory(Path.Combine(tenant, "application-setup", "app-setup-synthetic"));
        const string unknown = """{"schemaVersion":77,"status":"UnknownOutcome","futureMetadata":{"keep":"exact"},"integrityDigest":"historical-original-value"} """;
        File.WriteAllText(Path.Combine(tenant, "runs", "journal-synthetic.jsonl"), unknown + "\n");
        File.WriteAllText(Path.Combine(tenant, "managed-objects.json"), unknown);
        File.WriteAllText(Path.Combine(tenant, "application-setup", "app-setup-synthetic", "journal.ndjson"), unknown + "\n");
        File.WriteAllText(Path.Combine(tenant, "msal-assessment.cache"), "SYNTHETIC-TOKEN");
        File.WriteAllText(Path.Combine(tenant, "msal-assessment.cache.abc.tmp"), "SYNTHETIC-TOKEN");
        File.WriteAllText(Path.Combine(tenant, "policy-write.lock"), "");
        var records = Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories).Where(f => Path.GetExtension(f) is ".json" or ".jsonl" or ".ndjson").ToDictionary(f => Path.GetRelativePath(root.Paths.DataDirectory, f), File.ReadAllBytes);
        var backup = new WorkspaceBackup(root.Paths);
        var zip = backup.Create();
        using (var archive = ZipFile.OpenRead(zip)) Assert.DoesNotContain(archive.Entries, e => e.FullName.Contains(".cache", StringComparison.Ordinal) || e.FullName.EndsWith("policy-write.lock", StringComparison.Ordinal));
        var destination = Path.Combine(root.Paths.ReportsDirectory, "separate-restoration");
        backup.RestoreSeparate(zip, destination);
        foreach (var (name, bytes) in records)
        {
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(destination, "data", name)));
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root.Paths.DataDirectory, name)));
        }
        using var second = new TempRoot();
        foreach (var (name, bytes) in records)
        {
            var file = Path.Combine(second.Paths.DataDirectory, name); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllBytes(file, bytes);
        }
        Assert.Equal(TestData.TenantA, new EvidenceStore(second.Paths, NullLog.Instance).LoadProfiles().Single().TenantId);
        Assert.Equal(unknown, File.ReadAllText(Path.Combine(second.Paths.TenantDirectory(TestData.TenantA), "managed-objects.json")));
    }

    [Fact]
    public void Changed_archive_data_is_refused_and_neither_source_nor_existing_destination_is_overwritten()
    {
        using var root = new TempRoot(); File.WriteAllText(root.Paths.ProfilesFile, "[]\n");
        var backup = new WorkspaceBackup(root.Paths); var zip = backup.Create();
        var existing = Path.Combine(root.Paths.ReportsDirectory, "existing"); Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "keep");
        Assert.Throws<ConfigurationException>(() => backup.RestoreSeparate(zip, existing));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(existing, "keep.txt")));
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.GetEntry("data/profiles.json")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("data/profiles.json").Open()); writer.Write("changed");
        }
        var destination = Path.Combine(root.Paths.ReportsDirectory, "refused");
        Assert.Throws<IntegrityException>(() => backup.RestoreSeparate(zip, destination));
        Assert.False(Directory.Exists(destination)); Assert.Equal("[]\n", File.ReadAllText(root.Paths.ProfilesFile));
        Assert.Empty(Directory.GetDirectories(root.Paths.ReportsDirectory, ".restore-*"));
    }

    [Theory]
    [InlineData("data/../escape.json")][InlineData("/data/file.json")][InlineData("data/a\\b.json")][InlineData("data/CON.json")]
    [InlineData("data/file.json.")][InlineData("data/msal-assessment.cache")][InlineData("app/execute.exe")]
    public void Unsafe_or_prohibited_archive_entries_are_refused_without_extraction(string path)
    {
        using var root = new TempRoot(); var zip = Path.Combine(root.Root, "unsafe.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntry(path);
        var destination = Path.Combine(root.Root, "refused");
        Assert.Throws<IntegrityException>(() => new WorkspaceBackup(root.Paths).RestoreSeparate(zip, destination));
        Assert.False(Directory.Exists(destination)); Assert.False(File.Exists(Path.Combine(root.Root, "escape.json")));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Duplicate_or_unlisted_archive_entries_cannot_bypass_exact_checksum_coverage(bool duplicate)
    {
        using var root = new TempRoot(); File.WriteAllText(root.Paths.ProfilesFile, "[]");
        var backup = new WorkspaceBackup(root.Paths); var zip = backup.Create();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update)) archive.CreateEntry(duplicate ? "data/PROFILES.json" : "data/unlisted.json");
        Assert.Throws<IntegrityException>(() => backup.RestoreSeparate(zip, Path.Combine(root.Root, "refused")));
    }

    [Fact]
    public void Unsupported_source_files_stop_backup_instead_of_silently_losing_records()
    {
        using var root = new TempRoot(); File.WriteAllText(Path.Combine(root.Paths.DataDirectory, "new-format.bin"), "keep");
        Assert.Throws<ConfigurationException>(() => new WorkspaceBackup(root.Paths).Create());
        Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
    }

    [Fact]
    public void Data_links_are_refused_on_platforms_that_support_unprivileged_symbolic_links()
    {
        if (OperatingSystem.IsWindows()) return; // Windows junction coverage remains a native acceptance check.
        using var root = new TempRoot(); var outside = Path.Combine(root.Root, "outside.json"); File.WriteAllText(outside, "private");
        File.CreateSymbolicLink(Path.Combine(root.Paths.DataDirectory, "linked.json"), outside);
        Assert.Throws<IntegrityException>(() => new WorkspaceBackup(root.Paths).Create());
        Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
    }
}
