using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Collection;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Execution;
using BDIT.TenantToolkit.Engine.Planning;
using BDIT.TenantToolkit.Engine.Recovery;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class RecoveryTests
{
    [Fact]
    public void Tenant_write_lease_prevents_concurrent_processes_and_releases_after_disposal()
    {
        using var root = new TempRoot(); var first = new EvidenceStore(root.Paths, NullLog.Instance); var second = new EvidenceStore(root.Paths, NullLog.Instance);
        using (first.AcquireTenantWriteLease(TestData.TenantA))
            Assert.Throws<SafetyViolationException>(() => second.AcquireTenantWriteLease(TestData.TenantA));
        using var next = second.AcquireTenantWriteLease(TestData.TenantA);
    }
    internal sealed class Harness : IDisposable
    {
        public TempRoot Root { get; } = new();
        public FixedClock Clock { get; } = new();
        public StandardCatalogue Standard { get; } = TestData.Standard();
        public TenantProfile Profile { get; } = TestData.Profile();
        public TenantSession Session { get; } = TestData.Session();
        public EvidenceStore Evidence { get; }
        public FakeGraphClient Graph { get; }
        public RecoveryService Recovery { get; }
        public Harness()
        {
            Evidence = new(Root.Paths, NullLog.Instance); Graph = new(Standard); Recovery = new(Evidence, Clock);
            foreach (var (key, capture) in TestData.Snapshot(Standard).Collections)
                foreach (var item in capture.Items) Graph.Add(Standard.Collections[key].BasePath, (JsonObject)item.DeepClone());
        }
        public async Task<TenantSnapshot> Capture()
        {
            var snapshot = await new TenantCollector(NullLog.Instance, Clock, "test").CollectAsync(Graph, Session, Profile, Standard, null, CancellationToken.None);
            Evidence.SaveSnapshot(snapshot); return snapshot;
        }
        public async Task<DeploymentRun> Deploy(string id = "CA-001")
        {
            var snapshot = await Capture();
            var plan = new DeploymentPlanner(Clock, "test").Build(new PlanRequest
            {
                Profile = Profile, Standard = Standard, Snapshot = snapshot, Mappings = Evidence.LoadMappings(Profile.TenantId),
                Deviations = Array.Empty<Deviation>(), SelectedControlIds = new[] { id }, Session = Session
            });
            return await new DeploymentExecutor(Evidence, new TenantCollector(NullLog.Instance, Clock, "test"), NullLog.Instance, Clock, "test")
                .StartAsync(new ExecutionRequest { Plan = plan, Profile = Profile, Standard = Standard, Snapshot = snapshot,
                    Mappings = Evidence.LoadMappings(Profile.TenantId), Session = Session, Graph = Graph, AcknowledgedSnapshotId = snapshot.Id }, new(), null);
        }
        public async Task<RecoveryPlan> Preview(DeploymentRun run, RecoveryAction action = RecoveryAction.DeleteCreatedObject) =>
            await Recovery.PreviewAsync(Graph, Session, Standard, await Capture(), run.Id, run.Results.Single().ControlId, action, CancellationToken.None);
        public Task<RecoveryRun> Execute(RecoveryPlan plan, bool drift = false) =>
            Recovery.ExecuteAsync(Graph, Session, Standard, plan, Session.TenantId, true, drift, CancellationToken.None);
        public JsonObject Object(DeploymentRun run) => Graph.Collection(Standard.Collections[run.Results.Single().Collection].BasePath).Single(o => o["id"]?.GetValue<string>() == run.Results.Single().ObjectId);
        public void Dispose() => Root.Dispose();
    }

    [Theory]
    [InlineData("CA-001")]
    [InlineData("CMP-WIN-001")]
    public async Task Created_object_is_logged_then_selectively_removed_and_absence_verified(string control)
    {
        using var h = new Harness(); var deployment = await h.Deploy(control);
        Assert.Equal(RunStatus.Completed, deployment.Status);
        var item = deployment.Results.Single(); Assert.NotNull(item.WrittenPayload); Assert.NotNull(item.AfterObject);
        var plan = await h.Preview(deployment); Assert.False(plan.DriftDetected);
        h.Graph.BeforeRecovery = () =>
        {
            var intent = h.Evidence.LoadRecoveryRuns(h.Session.TenantId).Single();
            Assert.Equal(WriteAcceptance.Unknown, intent.WriteAcceptance); Assert.NotEmpty(intent.BeforeObject);
            return Task.CompletedTask;
        };
        var recovery = await h.Execute(plan);
        Assert.Equal(RunStatus.Completed, recovery.Status); Assert.Equal(WriteAcceptance.Accepted, recovery.WriteAcceptance);
        Assert.Equal(ConfigurationVerification.Pass, recovery.Verification); Assert.True(recovery.ObjectAbsent);
        Assert.Null(h.Evidence.LoadMappings(h.Session.TenantId).Find(control)); Assert.Single(h.Graph.RecoveryWrites);
        Assert.Equal(recovery.Id, h.Evidence.LoadRecoveryRuns(h.Session.TenantId).Single().Id);
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Execute(plan)); Assert.Single(h.Graph.RecoveryWrites);
    }

    [Fact]
    public async Task Wrong_tenant_read_only_or_unapproved_recovery_never_writes()
    {
        using var h = new Harness(); var run = await h.Deploy(); var plan = await h.Preview(run);
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Recovery.ExecuteAsync(h.Graph, h.Session, h.Standard, plan, TestData.TenantB, true, false, CancellationToken.None));
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Recovery.ExecuteAsync(h.Graph, h.Session, h.Standard, plan, h.Session.TenantId, false, false, CancellationToken.None));
        h.Graph.TenantId = TestData.TenantB; await Assert.ThrowsAsync<TenantMismatchException>(() => h.Execute(plan));
        h.Graph.TenantId = h.Session.TenantId; h.Graph.Mode = SessionMode.Assessment;
        await Assert.ThrowsAsync<WriteDeniedException>(() => h.Execute(plan)); Assert.Empty(h.Graph.RecoveryWrites);
    }

    [Fact]
    public async Task Missing_ownership_and_name_matches_cannot_authorise_removal()
    {
        using var h = new Harness(); var run = await h.Deploy();
        h.Evidence.SaveMappings(new ManagedObjectMappings { TenantId = h.Session.TenantId });
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Preview(run)); Assert.Empty(h.Graph.RecoveryWrites);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("operator")]
    [InlineData("client")]
    [InlineData("standard")]
    [InlineData("time")]
    [InlineData("payload")]
    public async Task Changed_review_inputs_block_recovery(string change)
    {
        using var h = new Harness(); var run = await h.Deploy(); var plan = await h.Preview(run);
        switch (change)
        {
            case "object": h.Object(run)["displayName"] = "Changed after review"; break;
            case "operator": h.Session.OperatorObjectId = TestData.Emergency; break;
            case "client": h.Session.ClientId = Guid.NewGuid().ToString(); break;
            case "standard": h.Standard.Description += "changed"; break;
            case "time": h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(6); break;
            case "payload": plan.ObjectId = Guid.NewGuid().ToString(); break;
        }
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Execute(plan)); Assert.Empty(h.Graph.RecoveryWrites);
    }

    [Fact]
    public async Task Unexpected_active_CA_requires_drift_approval_and_only_disables_state()
    {
        using var h = new Harness(); var source = await h.Deploy(); var obj = h.Object(source);
        obj["state"] = "enabled"; obj["conditions"]!["users"]!["includeUsers"] = new JsonArray(TestData.Operator);
        var conditions = obj["conditions"]!.ToJsonString();
        var plan = await h.Preview(source, RecoveryAction.DisableConditionalAccess);
        Assert.True(plan.DriftDetected);
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Execute(plan)); Assert.Empty(h.Graph.RecoveryWrites);
        var result = await h.Execute(plan, drift: true);
        Assert.Equal(ConfigurationVerification.Pass, result.Verification);
        Assert.Equal("disabled", obj["state"]!.GetValue<string>()); Assert.Equal(conditions, obj["conditions"]!.ToJsonString());
        Assert.Single(h.Graph.RecoveryWrites.Single().Payload!);
    }

    [Fact]
    public async Task Assigned_Intune_object_is_not_silently_unassigned_or_deleted()
    {
        using var h = new Harness(); var source = await h.Deploy("CMP-WIN-001");
        h.Object(source)["_assignments"] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid().ToString() });
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Preview(source)); Assert.Empty(h.Graph.RecoveryWrites);
    }

    [Fact]
    public async Task Ambiguous_recovery_is_recorded_and_blocks_replay_or_new_deployment()
    {
        using var h = new Harness(); var source = await h.Deploy(); var plan = await h.Preview(source);
        h.Graph.RecoveryError = new AmbiguousWriteException("network lost", null);
        var result = await h.Execute(plan);
        Assert.Equal(WriteAcceptance.Unknown, result.WriteAcceptance); Assert.Equal(RunStatus.ReviewRequired, result.Status);
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Execute(plan));
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Preview(source));
        Assert.Throws<SafetyViolationException>(() => h.Evidence.AssertNoUnresolvedRecovery(h.Session.TenantId, new[] { "CA-001" }));
        Assert.Single(h.Graph.RecoveryWrites);
    }

    [Fact]
    public async Task Accepted_recovery_without_expected_final_state_is_not_reported_as_verified()
    {
        using var h = new Harness(); var source = await h.Deploy(); var plan = await h.Preview(source); h.Graph.IgnoreRecovery = true;
        var result = await h.Execute(plan);
        Assert.Equal(WriteAcceptance.Accepted, result.WriteAcceptance); Assert.Equal(ConfigurationVerification.Unknown, result.Verification);
        Assert.Equal(RunStatus.ReviewRequired, result.Status); Assert.NotNull(h.Evidence.LoadMappings(h.Session.TenantId).Find("CA-001"));
    }

    [Fact]
    public async Task Restore_update_preserves_ID_and_reinstates_recorded_before_values()
    {
        using var h = new Harness(); var created = await h.Deploy(); var obj = h.Object(created); var before = (JsonObject)obj.DeepClone();
        var mapping = h.Evidence.LoadMappings(h.Session.TenantId).Find("CA-001")!;
        var update = ToolkitJson.Deserialize<DeploymentRun>(ToolkitJson.Serialize(created)); update.Id = Guid.NewGuid().ToString();
        var item = update.Results.Single(); item.PlannedAction = "Update"; item.BeforeObject = before; item.BeforeMapping = mapping;
        item.WrittenPayload = new JsonObject { ["displayName"] = "Updated label", ["state"] = "disabled" };
        obj["displayName"] = "Updated label"; item.AfterObject = (JsonObject)obj.DeepClone(); h.Evidence.SaveRun(update);
        var mappings = h.Evidence.LoadMappings(h.Session.TenantId); mappings.Find("CA-001")!.RunId = update.Id; h.Evidence.SaveMappings(mappings);
        var plan = await h.Preview(update, RecoveryAction.RestoreUpdate); var result = await h.Execute(plan);
        Assert.Equal(ConfigurationVerification.Pass, result.Verification); Assert.Equal(before["displayName"]!.ToJsonString(), obj["displayName"]!.ToJsonString());
        Assert.Equal(created.Results.Single().ObjectId, obj["id"]!.GetValue<string>());
        Assert.Equal(mapping.RunId, h.Evidence.LoadMappings(h.Session.TenantId).Find("CA-001")!.RunId);
    }

    [Fact]
    public async Task Corrupt_run_or_incomplete_snapshot_blocks_preview()
    {
        using var h = new Harness(); var source = await h.Deploy(); var snapshot = await h.Capture();
        snapshot.Complete = false; h.Evidence.SaveSnapshot(snapshot);
        await Assert.ThrowsAsync<PlanValidationException>(() => h.Recovery.PreviewAsync(h.Graph, h.Session, h.Standard, snapshot, source.Id, "CA-001", RecoveryAction.DeleteCreatedObject, CancellationToken.None));
        source.Results.Single().ObjectId = Guid.NewGuid().ToString();
        File.WriteAllText(h.Evidence.RunFile(source), ToolkitJson.Serialize(source));
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Preview(source)); Assert.Empty(h.Graph.RecoveryWrites);
    }

    [Fact]
    public async Task Confirmed_not_sent_recovery_is_not_locked_and_a_fresh_preview_is_allowed()
    {
        using var h = new Harness(); var source = await h.Deploy(); var plan = await h.Preview(source);
        h.Graph.RecoveryError = new WriteDeniedException("Route refused before transport");
        var run = await h.Execute(plan);
        Assert.Equal(WriteAcceptance.NotAttempted, run.WriteAcceptance); Assert.Equal(ConfigurationVerification.NotRun, run.Verification);
        Assert.Equal(RunStatus.ReviewRequired, run.Status);
        h.Evidence.AssertNoUnresolvedRecovery(h.Session.TenantId, new[] { "CA-001" });
        h.Graph.RecoveryError = null; Assert.NotNull(await h.Preview(source));
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Execute(plan)); // Original plans remain single-use.
    }

    [Fact]
    public async Task A_followup_exception_never_downgrades_accepted_recovery_to_not_sent()
    {
        using var h = new Harness(); var source = await h.Deploy(); h.Object(source)["state"] = "enabled";
        var plan = await h.Preview(source, RecoveryAction.DisableConditionalAccess);
        h.Graph.BeforeRecovery = () => { h.Graph.MutateReadback = _ => throw new WriteDeniedException("Follow-up read refused"); return Task.CompletedTask; };
        var run = await h.Execute(plan, true);
        Assert.Equal(WriteAcceptance.Accepted, run.WriteAcceptance); Assert.Equal(ConfigurationVerification.Unknown, run.Verification);
    }
}
