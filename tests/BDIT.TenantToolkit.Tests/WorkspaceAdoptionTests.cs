using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// CLA-20261006-04/05/15: handoff and upgrade move evidence through a trusted archive digest, a verified separate
/// restore and a verified adoption into an empty workspace, never a manual folder copy.
/// </summary>
public sealed class WorkspaceAdoptionTests
{
    private static DeploymentRun SaveUnknownRun(EvidenceStore store, string planId)
    {
        var run = new DeploymentRun { Id = Guid.NewGuid().ToString(), PlanId = planId, TenantId = TestData.TenantA,
            StartedAt = Timestamps.Format(DateTimeOffset.UtcNow), Status = RunStatus.ReviewRequired,
            Results = [new RunResult { ControlId = "CA-001", PlannedAction = nameof(PlanAction.Create), Status = ResultStatus.Error,
                WriteAcceptance = WriteAcceptance.Unknown, Configuration = ConfigurationVerification.Unknown }] };
        store.SaveRun(run);
        return run;
    }

    [Fact]
    public void Restore_refuses_an_archive_that_does_not_match_the_trusted_digest_before_staging_anything()
    {
        using var root = new TempRoot(); File.WriteAllText(root.Paths.ProfilesFile, "[]");
        var backup = new WorkspaceBackup(root.Paths); var zip = backup.Create();
        Assert.StartsWith(root.Paths.TransfersDirectory, zip, StringComparison.Ordinal);
        var digest = WorkspaceBackup.ArchiveDigest(zip);
        Assert.Equal(digest, File.ReadAllText(zip + ".sha256").Split(' ')[0]);

        var wrong = new string('0', 64);
        Assert.Throws<IntegrityException>(() => backup.RestoreSeparate(zip, Path.Combine(root.Root, "wrong"), wrong));
        Assert.Throws<ConfigurationException>(() => backup.RestoreSeparate(zip, Path.Combine(root.Root, "malformed"), "not-a-digest"));
        Assert.False(Directory.Exists(Path.Combine(root.Root, "wrong")));
        Assert.Empty(Directory.GetDirectories(root.Root, ".restore-*"));

        Assert.True(Directory.Exists(backup.RestoreSeparate(zip, Path.Combine(root.Root, "trusted"), digest.ToUpperInvariant())));
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("  {0}  ")]
    [InlineData("{0}  workspace-backup.zip")]
    [InlineData("{0} *workspace-backup.zip")]
    [InlineData("SHA256 (workspace-backup.zip) = {0}")]
    public void A_pasted_or_loaded_checksum_line_yields_its_digest(string format)
    {
        var digest = new string('a', 32) + new string('F', 32);
        Assert.Equal(digest.ToLowerInvariant(), WorkspaceBackup.NormaliseDigest(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, digest)));
    }

    [Fact]
    public void Text_without_exactly_one_digest_is_left_for_validation_to_refuse()
    {
        Assert.False(WorkspaceBackup.IsValidDigest(WorkspaceBackup.NormaliseDigest("not a digest")));
        Assert.False(WorkspaceBackup.IsValidDigest(WorkspaceBackup.NormaliseDigest(new string('a', 64) + " " + new string('b', 64))));
        Assert.False(WorkspaceBackup.IsValidDigest(WorkspaceBackup.NormaliseDigest(null)));
    }

    [Fact]
    public void Verify_restored_detects_changed_extra_and_missing_files()
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        store.SaveProfiles([TestData.Profile()]); SaveUnknownRun(store, Guid.NewGuid().ToString());
        var backup = new WorkspaceBackup(root.Paths);
        var restored = backup.RestoreSeparate(backup.Create(), Path.Combine(root.Root, "restored"));
        Assert.Equal(2, WorkspaceBackup.VerifyRestored(restored));

        var profiles = Path.Combine(restored, "data", "profiles.json");
        var original = File.ReadAllBytes(profiles);
        File.WriteAllText(profiles, "[]");
        Assert.Throws<IntegrityException>(() => WorkspaceBackup.VerifyRestored(restored));
        File.WriteAllBytes(profiles, original);

        var extra = Path.Combine(restored, "data", "extra.json"); File.WriteAllText(extra, "{}");
        Assert.Throws<IntegrityException>(() => WorkspaceBackup.VerifyRestored(restored));
        File.Delete(extra);

        File.Move(profiles, profiles + ".moved");
        Assert.Throws<IntegrityException>(() => WorkspaceBackup.VerifyRestored(restored));
    }

    [Fact]
    public void Adoption_into_an_empty_workspace_preserves_bytes_and_unresolved_blockers_without_restoring_authority()
    {
        using var source = new TempRoot();
        var store = new EvidenceStore(source.Paths, NullLog.Instance);
        store.SaveProfiles([TestData.Profile()]);
        var oldPlanId = Guid.NewGuid().ToString();
        var run = SaveUnknownRun(store, oldPlanId);
        var zip = new WorkspaceBackup(source.Paths).Create();

        using var target = new TempRoot(); // a newly extracted package: empty data/ with empty tenants/
        var message = new WorkspaceBackup(target.Paths).AdoptFromArchive(zip, WorkspaceBackup.ArchiveDigest(zip));
        Assert.Contains("Adopted 2 verified evidence file(s)", message);
        Assert.Empty(Directory.GetDirectories(target.Root, ".adopt-*"));

        foreach (var file in Directory.GetFiles(source.Paths.DataDirectory, "*.json", SearchOption.AllDirectories))
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(target.Paths.DataDirectory, Path.GetRelativePath(source.Paths.DataDirectory, file))));
        Assert.Empty(Directory.GetDirectories(target.Root, ".adopt-*"));

        var adoptedStore = new EvidenceStore(target.Paths, NullLog.Instance);
        Assert.Equal(run.IntegrityDigest, adoptedStore.RequireIntactRuns(TestData.TenantA).Single().IntegrityDigest);
        var plan = new DeploymentPlan { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, Rows = [new PlanRow { ControlId = "CA-001", Action = PlanAction.Create }] };
        Assert.Contains(run.Id, Assert.Throws<PlanValidationException>(() => adoptedStore.AssertPlanHasNotRun(plan)).Message);
        plan.Id = oldPlanId;
        Assert.Contains("already has a deployment attempt", Assert.Throws<PlanValidationException>(() => adoptedStore.AssertPlanHasNotRun(plan)).Message);
    }

    [Fact]
    public void Adoption_refuses_a_workspace_that_already_holds_evidence()
    {
        using var source = new TempRoot(); File.WriteAllText(source.Paths.ProfilesFile, "[]");
        var zip = new WorkspaceBackup(source.Paths).Create();
        using var occupied = new TempRoot(); File.WriteAllText(occupied.Paths.ProfilesFile, "[{\"existing\":true}]");
        Assert.Throws<ConfigurationException>(() => new WorkspaceBackup(occupied.Paths).AdoptFromArchive(zip, WorkspaceBackup.ArchiveDigest(zip)));
        Assert.Equal("[{\"existing\":true}]", File.ReadAllText(occupied.Paths.ProfilesFile));
        Assert.False(Directory.Exists(occupied.Paths.TransfersDirectory) && Directory.GetDirectories(occupied.Paths.TransfersDirectory).Length > 0);
    }

    [Fact]
    public void Adoption_is_bound_to_the_trusted_archive_digest_so_a_rewritten_checksum_list_cannot_pass()
    {
        // A restored folder only proves it agrees with the checksum list stored beside it, so adoption starts from the
        // archive. An archive whose content and internal checksums are both rewritten must be refused.
        using var source = new TempRoot(); File.WriteAllText(source.Paths.ProfilesFile, "[]");
        var original = new WorkspaceBackup(source.Paths).Create();
        var trusted = WorkspaceBackup.ArchiveDigest(original);
        var tampered = Path.Combine(source.Root, "tampered.zip"); File.Copy(original, tampered);
        const string forged = "[{\"tampered\":true}]";
        using (var archive = ZipFile.Open(tampered, ZipArchiveMode.Update))
        {
            archive.GetEntry("data/profiles.json")!.Delete();
            using (var writer = new StreamWriter(archive.CreateEntry("data/profiles.json").Open())) writer.Write(forged);
            var sums = archive.GetEntry("SHA256SUMS.txt")!; string list;
            using (var reader = new StreamReader(sums.Open())) list = reader.ReadToEnd();
            sums.Delete();
            var forgedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(forged)));
            list = string.Join('\n', list.Split('\n').Select(l => l.EndsWith("  data/profiles.json", StringComparison.Ordinal) ? forgedHash + "  data/profiles.json" : l));
            using (var writer = new StreamWriter(archive.CreateEntry("SHA256SUMS.txt").Open())) writer.Write(list);
        }
        using var empty = new TempRoot();
        Assert.Throws<IntegrityException>(() => new WorkspaceBackup(empty.Paths).AdoptFromArchive(tampered, trusted));
        Assert.Empty(Directory.GetFiles(empty.Paths.DataDirectory, "*", SearchOption.AllDirectories));
        // Without the trusted digest the forged archive is internally consistent: exactly why the interface demands
        // the digest or an explicit, logged acknowledgement.
        using var acknowledged = new TempRoot();
        new WorkspaceBackup(acknowledged.Paths).AdoptFromArchive(tampered, null);
        Assert.Equal(forged, File.ReadAllText(acknowledged.Paths.ProfilesFile));
    }

    [Fact]
    public async Task Headless_verify_restore_accepts_an_intact_folder_and_refuses_a_changed_one()
    {
        using var root = new TempRoot(); File.WriteAllText(root.Paths.ProfilesFile, "[]");
        var backup = new WorkspaceBackup(root.Paths);
        var restored = backup.RestoreSeparate(backup.Create(), Path.Combine(root.Root, "restored"));
        var (exit, output) = await RunCli("verify-restore", "--folder", restored);
        Assert.Equal(0, exit); Assert.Contains("1 evidence file(s)", output);
        File.WriteAllText(Path.Combine(restored, "data", "profiles.json"), "[{}]");
        (exit, _) = await RunCli("verify-restore", "--folder", restored);
        Assert.Equal(2, exit);
    }

    private static async Task<(int Exit, string Output)> RunCli(params string[] arguments)
    {
        var configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        var cli = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../src/BDIT.TenantToolkit.Cli/bin/{configuration}/net10.0/bdit.dll"));
        Assert.True(File.Exists(cli), "The referenced CLI project must be built for this integration check.");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(cli); foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        await stderr;
        return (process.ExitCode, await stdout);
    }

    [Fact]
    public void Backup_is_refused_while_another_process_holds_a_tenant_write_lease()
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        store.SaveProfiles([TestData.Profile()]);
        using (store.AcquireTenantWriteLease(TestData.TenantA))
        {
            Assert.Contains("in progress", Assert.Throws<ConfigurationException>(() => new WorkspaceBackup(root.Paths).Create()).Message);
            Assert.True(!Directory.Exists(root.Paths.TransfersDirectory) || Directory.GetFiles(root.Paths.TransfersDirectory).Length == 0);
        }
        Assert.True(File.Exists(new WorkspaceBackup(root.Paths).Create()));
    }
}
