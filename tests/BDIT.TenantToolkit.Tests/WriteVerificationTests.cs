using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Recovery;
using Xunit;
using Harness = BDIT.TenantToolkit.Tests.RecoveryTests.Harness;

namespace BDIT.TenantToolkit.Tests;

public sealed class WriteVerificationTests
{
    private static WriteVerificationService Service(Harness h) => new(h.Evidence, h.Clock);

    [Theory]
    [InlineData("CA-001")]
    [InlineData("CMP-WIN-001")]
    public async Task Accepted_delete_can_be_reverified_after_replication_without_repeating_the_write(string control)
    {
        using var h = new Harness(); var source = await h.Deploy(control); var plan = await h.Preview(source);
        h.Graph.IgnoreRecovery = true; var run = await h.Execute(plan); var original = ToolkitJson.Serialize(run);
        var pending = await Service(h).VerifyRecoveryAsync(h.Graph, h.Session, h.Standard, run.Id, CancellationToken.None);
        Assert.False(pending.Verified); Assert.NotNull(h.Evidence.LoadMappings(h.Session.TenantId).Find(control));
        Assert.Throws<SafetyViolationException>(() => h.Evidence.AssertNoUnresolvedRecovery(h.Session.TenantId, new[] { control }));
        h.Graph.Collection(h.Standard.Collections[source.Results.Single().Collection].BasePath).Clear();
        h.Graph.Mode = SessionMode.Assessment; h.Session.Mode = SessionMode.Assessment;
        var verified = await Service(h).VerifyRecoveryAsync(h.Graph, h.Session, h.Standard, run.Id, CancellationToken.None);
        Assert.True(verified.Verified); Assert.True(verified.ObjectAbsent); Assert.Null(h.Evidence.LoadMappings(h.Session.TenantId).Find(control));
        h.Evidence.AssertNoUnresolvedRecovery(h.Session.TenantId, new[] { control });
        Assert.Single(h.Graph.RecoveryWrites); Assert.Single(h.Graph.Writes);
        Assert.Equal(original, ToolkitJson.Serialize(h.Evidence.LoadRecoveryRuns(h.Session.TenantId).Single()));
        Assert.Equal(2, h.Evidence.LoadVerifications(h.Session.TenantId).Count);
    }

    [Fact]
    public async Task Already_removed_mapping_can_finish_verification_after_interrupted_local_finalisation()
    {
        using var h = new Harness(); var source = await h.Deploy(); var plan = await h.Preview(source);
        h.Graph.IgnoreRecovery = true; var run = await h.Execute(plan);
        h.Graph.Collection(h.Standard.Collections["conditionalAccess"].BasePath).Clear();
        var mappings = h.Evidence.LoadMappings(h.Session.TenantId); mappings.ByControl.Clear(); h.Evidence.SaveMappings(mappings);
        Assert.True((await Service(h).VerifyRecoveryAsync(h.Graph, h.Session, h.Standard, run.Id, CancellationToken.None)).Verified);
        Assert.Single(h.Graph.RecoveryWrites);
    }

    [Fact]
    public async Task Unknown_recovery_is_not_resolved_by_absence()
    {
        using var h = new Harness(); var source = await h.Deploy(); var plan = await h.Preview(source);
        h.Graph.RecoveryError = new AmbiguousWriteException("Unknown", null); var run = await h.Execute(plan);
        h.Graph.Collection(h.Standard.Collections["conditionalAccess"].BasePath).Clear();
        await Assert.ThrowsAsync<SafetyViolationException>(() => Service(h).VerifyRecoveryAsync(h.Graph, h.Session, h.Standard, run.Id, CancellationToken.None));
        Assert.Empty(h.Evidence.LoadVerifications(h.Session.TenantId));
    }

    [Fact]
    public async Task Changed_owner_or_wrong_tenant_never_reconciles_mapping()
    {
        using var h = new Harness(); var source = await h.Deploy(); var plan = await h.Preview(source);
        h.Graph.IgnoreRecovery = true; var run = await h.Execute(plan);
        h.Graph.TenantId = TestData.TenantB;
        await Assert.ThrowsAsync<TenantMismatchException>(() => Service(h).VerifyRecoveryAsync(h.Graph, h.Session, h.Standard, run.Id, CancellationToken.None));
        h.Graph.TenantId = h.Session.TenantId;
        var mappings = h.Evidence.LoadMappings(h.Session.TenantId); mappings.Find("CA-001")!.ObjectId = TestData.Operator; h.Evidence.SaveMappings(mappings);
        await Assert.ThrowsAsync<SafetyViolationException>(() => Service(h).VerifyRecoveryAsync(h.Graph, h.Session, h.Standard, run.Id, CancellationToken.None));
        Assert.Equal(TestData.Operator, h.Evidence.LoadMappings(h.Session.TenantId).Find("CA-001")!.ObjectId);
    }

    [Fact]
    public async Task Accepted_deployment_readback_can_be_verified_without_changing_original_run()
    {
        using var h = new Harness(); h.Graph.MutateReadback = obj => { obj["state"] = "enabled"; return obj; };
        var source = await h.Deploy(); var original = File.ReadAllText(h.Evidence.RunFile(source));
        Assert.Equal(ConfigurationVerification.Unknown, source.Results.Single().Configuration);
        h.Graph.MutateReadback = null; h.Graph.Mode = SessionMode.Assessment; h.Session.Mode = SessionMode.Assessment;
        var result = await Service(h).VerifyDeploymentAsync(h.Graph, h.Session, h.Standard, source.Id, "CA-001", false, CancellationToken.None);
        Assert.True(result.Verified); Assert.Single(h.Graph.Writes); Assert.Empty(h.Graph.RecoveryWrites);
        Assert.Equal(original, File.ReadAllText(h.Evidence.RunFile(source)));
        Assert.Equal(ConfigurationVerification.Pass, h.Recovery.Register(h.Session.TenantId).Single().Verification);
    }

    [Theory]
    [InlineData("enabled")]
    [InlineData("wrongName")]
    public async Task Deployment_reverification_does_not_accept_drift(string change)
    {
        using var h = new Harness(); var source = await h.Deploy(); h.Object(source)[change == "enabled" ? "state" : "displayName"] = change;
        var result = await Service(h).VerifyDeploymentAsync(h.Graph, h.Session, h.Standard, source.Id, "CA-001", false, CancellationToken.None);
        Assert.False(result.Verified); Assert.Single(h.Graph.Writes);
    }

    [Fact]
    public async Task Legacy_completion_requires_acknowledgement_exact_mapping_and_live_read_before_unlocking()
    {
        using var h = new Harness(); var source = await h.Deploy(); var item = source.Results.Single();
        source.ToolkitVersion = "1.0.0"; item.RecordedWriteAcceptance = null; item.WrittenPayload = null; item.AfterObject = null; h.Evidence.SaveRun(source);
        var original = File.ReadAllText(h.Evidence.RunFile(source));
        var next = new DeploymentPlan { Id = Guid.NewGuid().ToString(), TenantId = h.Session.TenantId,
            Rows = new() { new PlanRow { ControlId = "CA-001", Action = PlanAction.Update } } };
        Assert.Throws<PlanValidationException>(() => h.Evidence.AssertPlanHasNotRun(next));
        await Assert.ThrowsAsync<SafetyViolationException>(() => Service(h).VerifyDeploymentAsync(h.Graph, h.Session, h.Standard, source.Id, "CA-001", false, CancellationToken.None));
        var verified = await Service(h).VerifyDeploymentAsync(h.Graph, h.Session, h.Standard, source.Id, "CA-001", true, CancellationToken.None);
        Assert.True(verified.Verified); Assert.True(verified.HistoricalAcknowledged); h.Evidence.AssertPlanHasNotRun(next);
        Assert.Equal(original, File.ReadAllText(h.Evidence.RunFile(source)));
        Assert.Equal(WriteAcceptance.Unknown, h.Evidence.LoadRun(h.Session.TenantId, source.Id)!.Results.Single().WriteAcceptance);
        Assert.True(h.Evidence.HasAcceptedWrite(source, item));
    }

    [Theory]
    [InlineData("1.0.0", "Error", null)]
    [InlineData("1.0.0", "Completed", "Unknown")]
    [InlineData("1.1.0-preview.2", "Completed", null)]
    public async Task Historical_acknowledgement_is_not_a_general_override(string version, string status, string? acceptance)
    {
        using var h = new Harness(); var source = await h.Deploy(); source.ToolkitVersion = version;
        source.Results.Single().Status = status; source.Results.Single().RecordedWriteAcceptance = acceptance; h.Evidence.SaveRun(source);
        await Assert.ThrowsAsync<SafetyViolationException>(() => Service(h).VerifyDeploymentAsync(h.Graph, h.Session, h.Standard, source.Id, "CA-001", true, CancellationToken.None));
    }

    [Fact]
    public async Task Accepted_disablement_can_be_reverified_but_does_not_restore_targeting()
    {
        using var h = new Harness(); var source = await h.Deploy(); var obj = h.Object(source);
        obj["state"] = "enabled"; var targeting = obj["conditions"]!.ToJsonString();
        var plan = await h.Preview(source, RecoveryAction.DisableConditionalAccess);
        h.Graph.BeforeRecovery = () => { h.Graph.MutateReadback = _ => throw new IOException("Read unavailable"); return Task.CompletedTask; };
        var run = await h.Execute(plan, true); Assert.Equal(ConfigurationVerification.Unknown, run.Verification);
        h.Graph.MutateReadback = null;
        var record = await Service(h).VerifyRecoveryAsync(h.Graph, h.Session, h.Standard, run.Id, CancellationToken.None);
        Assert.True(record.Verified); Assert.Equal(targeting, obj["conditions"]!.ToJsonString());
        Assert.Equal(source.Id, h.Evidence.LoadMappings(h.Session.TenantId).Find("CA-001")!.RunId);
        Assert.Single(h.Graph.RecoveryWrites);
    }

    [Fact]
    public async Task Accepted_restore_can_finish_mapping_reconciliation_without_repeating_patch()
    {
        using var h = new Harness(); var created = await h.Deploy(); var obj = h.Object(created);
        var mappings = h.Evidence.LoadMappings(h.Session.TenantId); var beforeMapping = mappings.Find("CA-001")!;
        var update = ToolkitJson.Deserialize<DeploymentRun>(ToolkitJson.Serialize(created)); update.Id = Guid.NewGuid().ToString();
        var item = update.Results.Single(); item.PlannedAction = "Update";
        item.BeforeObject = (JsonObject)obj.DeepClone(); item.BeforeMapping = beforeMapping;
        item.WrittenPayload = new JsonObject { ["displayName"] = "Updated", ["state"] = "disabled" };
        item.PayloadDigest = CanonicalJson.Sha256(item.WrittenPayload);
        obj["displayName"] = "Updated"; item.AfterObject = (JsonObject)obj.DeepClone(); h.Evidence.SaveRun(update);
        mappings = h.Evidence.LoadMappings(h.Session.TenantId); var current = mappings.Find("CA-001")!;
        current.RunId = update.Id; current.LastApplied = (JsonObject)item.WrittenPayload.DeepClone(); current.LastAppliedDigest = item.PayloadDigest;
        h.Evidence.SaveMappings(mappings);
        var plan = await h.Preview(update, RecoveryAction.RestoreUpdate);
        h.Graph.BeforeRecovery = () => { h.Graph.MutateReadback = _ => throw new IOException("Read unavailable"); return Task.CompletedTask; };
        var run = await h.Execute(plan); Assert.Equal(ConfigurationVerification.Unknown, run.Verification);
        Assert.Equal(update.Id, h.Evidence.LoadMappings(h.Session.TenantId).Find("CA-001")!.RunId);
        h.Graph.MutateReadback = null;
        var record = await Service(h).VerifyRecoveryAsync(h.Graph, h.Session, h.Standard, run.Id, CancellationToken.None);
        Assert.True(record.Verified); Assert.Equal(created.Id, h.Evidence.LoadMappings(h.Session.TenantId).Find("CA-001")!.RunId);
        Assert.Single(h.Graph.RecoveryWrites);
        Assert.True((await Service(h).VerifyRecoveryAsync(h.Graph, h.Session, h.Standard, run.Id, CancellationToken.None)).Verified);
        Assert.Single(h.Graph.RecoveryWrites);
    }

    [Fact]
    public async Task Modified_verification_evidence_cannot_clear_a_recovery_block()
    {
        using var h = new Harness(); var source = await h.Deploy(); var plan = await h.Preview(source);
        h.Graph.IgnoreRecovery = true; var run = await h.Execute(plan);
        var record = await Service(h).VerifyRecoveryAsync(h.Graph, h.Session, h.Standard, run.Id, CancellationToken.None);
        record.Verified = true;
        File.WriteAllText(Path.Combine(h.Evidence.TenantDirectory(h.Session.TenantId), "verification", record.Id + ".json"), ToolkitJson.Serialize(record));
        Assert.Throws<IntegrityException>(() => h.Evidence.AssertNoUnresolvedRecovery(h.Session.TenantId, new[] { "CA-001" }));
        Assert.NotNull(h.Evidence.LoadMappings(h.Session.TenantId).Find("CA-001"));
    }

    [Fact]
    public async Task Cancelled_verification_preserves_block_and_records_incomplete_attempt()
    {
        using var h = new Harness(); var source = await h.Deploy(); var plan = await h.Preview(source);
        h.Graph.IgnoreRecovery = true; var run = await h.Execute(plan);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        var result = await Service(h).VerifyRecoveryAsync(h.Graph, h.Session, h.Standard, run.Id, stop.Token);
        Assert.False(result.Verified); Assert.Contains("cancelled", result.Detail);
        Assert.Throws<SafetyViolationException>(() => h.Evidence.AssertNoUnresolvedRecovery(h.Session.TenantId, new[] { "CA-001" }));
        Assert.Single(h.Graph.RecoveryWrites);
    }
}
