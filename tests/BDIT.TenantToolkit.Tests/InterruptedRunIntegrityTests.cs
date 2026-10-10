using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class InterruptedRunIntegrityTests
{
    private static DeploymentRun Running() => new()
    {
        Id = Guid.NewGuid().ToString("D"), TenantId = TestData.TenantA,
        PlanId = Guid.NewGuid().ToString("D"), StartedAt = "2026-10-10T09:00:00.000Z",
        Status = RunStatus.Running,
        Results = [new() { ControlId = "CA-001", PlannedAction = "Create", Status = ResultStatus.InProgress,
            WriteAcceptance = WriteAcceptance.Unknown }]
    };

    [Fact]
    public void Startup_cannot_reseal_an_edited_unknown_write_as_not_attempted()
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var run = Running(); store.SaveRun(run);
        var file = store.RunFile(run);
        var edited = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        edited["results"]![0]!["status"] = ResultStatus.Pending;
        // Simulate an unsealed local edit, not a forged/authenticated record. The original digest is retained.
        File.WriteAllText(file, edited.ToJsonString());
        var originalBytes = File.ReadAllBytes(file);
        Assert.Throws<PlanValidationException>(() => store.AssertCompletionWritesResolved(TestData.TenantA, ["CA-001"]));

        var error = Record.Exception(() => store.MarkInterruptedRuns(TestData.TenantA));

        Assert.Throws<PlanValidationException>(() => store.AssertCompletionWritesResolved(TestData.TenantA, ["CA-001"]));
        Assert.Equal(originalBytes, File.ReadAllBytes(file));
        Assert.IsType<PlanValidationException>(error);
        Assert.False(EvidenceIntegrity.Verify(store.LoadRun(TestData.TenantA, run.Id)!, run.IntegrityDigest));
    }

    [Theory]
    [InlineData("missing-digest")]
    [InlineData("invalid-json")]
    [InlineData("empty-record")]
    [InlineData("wrong-tenant")]
    public void Startup_refuses_unreadable_history_without_changing_any_record(string fault)
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var valid = Running(); store.SaveRun(valid);
        var broken = Running(); store.SaveRun(broken);
        var file = store.RunFile(broken);
        var node = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        if (fault == "missing-digest") node.Remove("integrityDigest");
        if (fault == "wrong-tenant") node["tenantId"] = TestData.TenantB;
        File.WriteAllText(file, fault == "invalid-json" ? "{" : fault == "empty-record" ? "null" : node.ToJsonString());
        var validBytes = File.ReadAllBytes(store.RunFile(valid)); var brokenBytes = File.ReadAllBytes(file);

        var error = Record.Exception(() => store.MarkInterruptedRuns(TestData.TenantA));

        Assert.IsAssignableFrom<ToolkitException>(error);
        Assert.Equal(validBytes, File.ReadAllBytes(store.RunFile(valid)));
        Assert.Equal(brokenBytes, File.ReadAllBytes(file));
    }

    [Fact]
    public void Verified_interrupted_history_stays_unresolved_and_the_old_plan_stays_single_use()
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var run = Running();
        run.Results.Add(new() { ControlId = "CA-003", PlannedAction = "Create", Status = ResultStatus.Pending });
        store.SaveRun(run);

        Assert.Equal(1, store.MarkInterruptedRuns(TestData.TenantA));
        var loaded = store.LoadRun(TestData.TenantA, run.Id)!;
        Assert.True(EvidenceIntegrity.Verify(loaded, loaded.IntegrityDigest));
        Assert.Equal(RunStatus.Interrupted, loaded.Status);
        Assert.Equal(WriteAcceptance.Unknown, loaded.Results[0].WriteAcceptance);
        Assert.Equal(ResultStatus.Error, loaded.Results[0].Status);
        Assert.Equal(WriteAcceptance.NotAttempted, loaded.Results[1].WriteAcceptance);
        Assert.Equal(ResultStatus.NotRun, loaded.Results[1].Status);
        Assert.Throws<PlanValidationException>(() => store.AssertCompletionWritesResolved(TestData.TenantA, ["CA-001"]));
        store.AssertCompletionWritesResolved(TestData.TenantA, ["CA-003"]);
        Assert.Throws<PlanValidationException>(() => store.AssertPlanHasNotRun(new() { Id = run.PlanId, TenantId = TestData.TenantA }));
        var bytes = File.ReadAllBytes(store.RunFile(run));
        Assert.Equal(0, store.MarkInterruptedRuns(TestData.TenantA));
        Assert.Equal(bytes, File.ReadAllBytes(store.RunFile(run)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entire_history_is_validated_before_any_marking_write_regardless_of_order(bool damagedFirst)
    {
        using var root = new TempRoot();
        var writer = new EvidenceStore(root.Paths, NullLog.Instance);
        var valid = Running(); writer.SaveRun(valid);
        var damaged = Running(); writer.SaveRun(damaged);
        var validFile = writer.RunFile(valid); var damagedFile = writer.RunFile(damaged);
        var node = JsonNode.Parse(File.ReadAllText(damagedFile))!.AsObject();
        node["results"]![0]!["status"] = ResultStatus.Pending;
        File.WriteAllText(damagedFile, node.ToJsonString());
        var validBytes = File.ReadAllBytes(validFile); var damagedBytes = File.ReadAllBytes(damagedFile);
        var ordered = damagedFirst ? new[] { damagedFile, validFile } : new[] { validFile, damagedFile };
        var enumerations = 0;
        var store = new EvidenceStore(root.Paths, NullLog.Instance, directory =>
        {
            Assert.Equal(Path.GetDirectoryName(validFile), directory); enumerations++; return ordered;
        });

        Assert.Throws<PlanValidationException>(() => store.MarkInterruptedRuns(TestData.TenantA));

        Assert.Equal(1, enumerations);
        Assert.Equal(validBytes, File.ReadAllBytes(validFile));
        Assert.Equal(damagedBytes, File.ReadAllBytes(damagedFile));
    }

    [Fact]
    public void Startup_respects_another_store_active_tenant_lease_without_rewriting_the_run()
    {
        using var root = new TempRoot();
        var active = new EvidenceStore(root.Paths, NullLog.Instance);
        var startup = new EvidenceStore(root.Paths, NullLog.Instance);
        var run = Running(); active.SaveRun(run);
        var file = active.RunFile(run); var bytes = File.ReadAllBytes(file);
        using (active.AcquireTenantWriteLease(TestData.TenantA))
        {
            var refusal = Assert.Throws<SafetyViolationException>(() => startup.MarkInterruptedRuns(TestData.TenantA));
            Assert.True(EvidenceStore.IsTenantWriteLeaseRefusal(refusal));
            Assert.StartsWith("Another deployment or recovery is using this tenant's evidence store. Wait for it to finish. ", refusal.Message);
            Assert.False(EvidenceStore.IsTenantWriteLeaseRefusal(new SafetyViolationException(refusal.Message)));
            Assert.False(EvidenceStore.IsTenantWriteLeaseRefusal(new SafetyViolationException("Deployment evidence was modified; recovery is blocked.")));
            Assert.False(EvidenceStore.IsTenantWriteLeaseRefusal(new IOException("unavailable")));
            Assert.Equal(bytes, File.ReadAllBytes(file));
        }
        Assert.Equal(1, startup.MarkInterruptedRuns(TestData.TenantA));
        var loaded = startup.LoadRun(TestData.TenantA, run.Id)!;
        Assert.Equal(RunStatus.Interrupted, loaded.Status);
        Assert.Equal(ResultStatus.Error, loaded.Results[0].Status);
        Assert.Equal(WriteAcceptance.Unknown, loaded.Results[0].WriteAcceptance);
        Assert.True(EvidenceIntegrity.Verify(loaded, loaded.IntegrityDigest));
        var interruptedBytes = File.ReadAllBytes(file);
        Assert.Equal(0, startup.MarkInterruptedRuns(TestData.TenantA));
        Assert.Equal(interruptedBytes, File.ReadAllBytes(file));
    }

}
