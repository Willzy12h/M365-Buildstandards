using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Collection;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Execution;
using BDIT.TenantToolkit.Engine.Planning;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public class ExecutorTests
{
    private sealed class Harness : IDisposable
    {
        public TempRoot Root { get; } = new();
        public FixedClock Clock { get; } = new();
        public StandardCatalogue Standard { get; } = TestData.Standard();
        public EvidenceStore Evidence { get; }
        public FakeGraphClient Graph { get; }
        public TenantCollector Collector { get; }
        public DeploymentExecutor Executor { get; }
        public TenantProfile Profile { get; } = TestData.Profile();
        public TenantSession Session { get; } = TestData.Session();

        public Harness()
        {
            Evidence = new EvidenceStore(Root.Paths, NullLog.Instance);
            Graph = new FakeGraphClient(Standard);
            foreach (var (key, capture) in TestData.Snapshot(Standard).Collections)
                foreach (var item in capture.Items) Graph.Add(Standard.Collections[key].BasePath, (JsonObject)item.DeepClone());
            Collector = new TenantCollector(NullLog.Instance, Clock, "test");
            Executor = new DeploymentExecutor(Evidence, Collector, NullLog.Instance, Clock, "test");
        }

        public async Task<(TenantSnapshot Snapshot, DeploymentPlan Plan)> CaptureAndPlanAsync(params string[] ids)
        {
            var snapshot = await Collector.CollectAsync(Graph, Session, Profile, Standard, null, CancellationToken.None);
            Evidence.SaveSnapshot(snapshot);
            var plan = new DeploymentPlanner(Clock, "test").Build(new PlanRequest
            {
                Profile = Profile, Standard = Standard, Snapshot = snapshot, Mappings = Evidence.LoadMappings(Profile.TenantId),
                Deviations = Array.Empty<Deviation>(), SelectedControlIds = ids, Session = Session
            });
            return (snapshot, plan);
        }

        public Task<DeploymentRun> RunAsync(DeploymentPlan plan, TenantSnapshot snapshot, DeploymentControl? control = null) =>
            Executor.StartAsync(new ExecutionRequest
            {
                Plan = plan, Profile = Profile, Standard = Standard, Snapshot = snapshot, Mappings = Evidence.LoadMappings(Profile.TenantId), Session = Session, Graph = Graph,
                AcknowledgedSnapshotId = snapshot.Id
            }, control ?? new DeploymentControl(), null);

        public void Dispose() => Root.Dispose();
    }

    [Fact]
    public async Task Successful_run_writes_reads_back_maps_and_captures_after_evidence()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001", "CMP-WIN-001");
        var run = await h.RunAsync(plan, snapshot);

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(2, h.Graph.Writes.Count);
        Assert.All(run.Results, r => Assert.Equal(ResultStatus.Completed, r.Status));
        Assert.All(run.Results, r => Assert.Equal(ConfigurationVerification.Pass, r.Configuration));
        Assert.All(run.Results, r => Assert.Equal(WriteAcceptance.Accepted, r.WriteAcceptance));
        Assert.Contains(run.Results, r => r.Verification.Contains("Functional", StringComparison.Ordinal));
        Assert.NotNull(run.AfterSnapshotId);
        Assert.NotNull(h.Evidence.LoadSnapshot(TestData.TenantA, run.AfterSnapshotId!));
        Assert.NotNull(h.Evidence.LoadRun(TestData.TenantA, run.Id));

        var mappings = h.Evidence.LoadMappings(TestData.TenantA);
        Assert.Equal(run.Results[0].ObjectId, mappings.ByControl["CA-001"].ObjectId);
        Assert.Equal(TestData.Operator, mappings.ByControl["CA-001"].OperatorExclusion!.ObjectId);

        var written = h.Graph.Writes[0].Payload;
        Assert.Equal("disabled", ConditionalAccessSafety.State(written));
        Assert.Contains(TestData.Operator, ConditionalAccessSafety.ExcludedUsers(written));
        Assert.Contains(h.Evidence.ReadJournal(TestData.TenantA, run.Id), j => j.Message.Contains("WRITE INTENT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_write_intent_is_durable_before_the_graph_request()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001");
        RunResult? savedAtRequest = null;
        h.Graph.BeforeWrite = (_, _) =>
        {
            savedAtRequest = h.Evidence.LoadRun(TestData.TenantA, h.Executor.CurrentRun!.Id)!.Results.Single();
            return Task.CompletedTask;
        };
        var run = await h.RunAsync(plan, snapshot);
        Assert.True(run.Status == RunStatus.Completed, ToolkitJson.Serialize(run));
        Assert.NotNull(savedAtRequest);
        Assert.Equal(ResultStatus.InProgress, savedAtRequest.Status);
        Assert.Equal(WriteAcceptance.Unknown, savedAtRequest.WriteAcceptance);
        Assert.Equal(ConfigurationVerification.Unknown, savedAtRequest.Configuration);
        Assert.NotNull(savedAtRequest.PayloadDigest);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("tampered-file")]
    [InlineData("changed-memory")]
    [InlineData("incomplete")]
    [InlineData("unrelated-failure")]
    [InlineData("missing-collection")]
    [InlineData("missing-details")]
    [InlineData("wrong-route")]
    [InlineData("count-mismatch")]
    public async Task Executor_rejects_missing_or_incomplete_durable_evidence_before_graph_access(string fault)
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001");
        var file = Assert.Single(h.Evidence.ListSnapshots(TestData.TenantA)).File;
        switch (fault)
        {
            case "missing": File.Delete(file); break;
            case "tampered-file": File.WriteAllText(file, File.ReadAllText(file).Replace("\"complete\": true", "\"complete\": false", StringComparison.Ordinal)); break;
            case "changed-memory": snapshot.TenantName = "Changed without resaving"; break;
            default:
                switch (fault)
                {
                    case "incomplete": snapshot.Complete = false; break;
                    case "unrelated-failure": snapshot.Collections["groups"].Status = CaptureStatus.Error; break;
                    case "missing-collection": snapshot.Collections.Remove("groups"); break;
                    case "missing-details":
                        snapshot.Collections["compliance"].Items.Add(new JsonObject { ["id"] = "synthetic-policy" });
                        snapshot.Collections["compliance"].Count = 1;
                        break;
                    case "wrong-route": snapshot.Collections["users"].Path = "/groups"; break;
                    case "count-mismatch": snapshot.Collections["users"].Count++; break;
                }
                h.Evidence.SaveSnapshot(snapshot);
                plan.SnapshotDigest = DeploymentPlanner.SnapshotDigest(snapshot);
                plan.PlanDigest = DeploymentPlanner.ComputeDigest(plan);
                break;
        }
        h.Graph.Reads.Clear();
        await Assert.ThrowsAnyAsync<ToolkitException>(() => h.RunAsync(plan, snapshot));
        Assert.Empty(h.Graph.Writes);
        Assert.Empty(h.Graph.Reads);
    }

    [Theory]
    [InlineData("operator")]
    [InlineData("tenant-verification")]
    [InlineData("operator-verification")]
    [InlineData("standard")]
    [InlineData("plan")]
    [InlineData("deviation")]
    [InlineData("expired")]
    public async Task Executor_checks_current_bindings_without_relying_on_the_workspace(string fault)
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001");
        switch (fault)
        {
            case "operator": h.Session.Account = "other@test.example"; break;
            case "tenant-verification": h.Session.TenantVerified = false; break;
            case "operator-verification": h.Session.OperatorVerified = false; break;
            case "standard": h.Standard.Controls[0].Name = "Changed without clearing source digest"; break;
            case "plan": plan.Rows[0].Payload!["displayName"] = "Unreviewed name"; break;
            case "deviation": h.Evidence.SaveDeviations(TestData.TenantA, new[] { new Deviation { TenantId = TestData.TenantA, ControlId = "CA-001", Reason = "Now excluded" } }); break;
            case "expired": h.Clock.UtcNow = h.Clock.UtcNow.AddHours(1); break;
        }
        h.Graph.Reads.Clear();
        await Assert.ThrowsAnyAsync<ToolkitException>(() => h.RunAsync(plan, snapshot));
        Assert.Empty(h.Graph.Writes);
        Assert.Empty(h.Graph.Reads);
    }

    [Fact]
    public async Task Executor_requires_explicit_snapshot_acknowledgement()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001");
        await Assert.ThrowsAsync<PlanValidationException>(() => h.Executor.StartAsync(new ExecutionRequest
        {
            Plan = plan, Snapshot = snapshot, Profile = h.Profile, Standard = h.Standard, Mappings = h.Evidence.LoadMappings(TestData.TenantA),
            Session = h.Session, Graph = h.Graph
        }, new DeploymentControl(), null));
        Assert.Empty(h.Graph.Writes);
    }

    [Fact]
    public async Task Unexpected_write_failure_leaves_no_pending_results_and_is_never_retried()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001", "CA-003");
        h.Graph.ThrowOnWrite = new InvalidOperationException("Unexpected transport failure");
        var run = await h.RunAsync(plan, snapshot);
        Assert.Equal(RunStatus.ReviewRequired, run.Status);
        Assert.Single(h.Graph.Writes);
        Assert.Equal(ResultStatus.Error, run.Results[0].Status);
        Assert.Equal(WriteAcceptance.Unknown, run.Results[0].WriteAcceptance);
        Assert.Equal(ConfigurationVerification.Unknown, run.Results[0].Configuration);
        Assert.Equal(ResultStatus.NotRun, run.Results[1].Status);
        Assert.Equal(ConfigurationVerification.NotRun, run.Results[1].Configuration);
        Assert.All(run.Results, r => Assert.DoesNotContain(r.Status, new[] { ResultStatus.Pending, ResultStatus.InProgress }));
        Assert.Equal(ResultStatus.Error, h.Evidence.LoadRun(TestData.TenantA, run.Id)!.Results[0].Status);
    }

    [Fact]
    public async Task Accepted_write_remains_accepted_when_readback_throws_unexpectedly()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001", "CA-003");
        h.Graph.MutateReadback = _ => throw new FormatException("Readback could not be parsed");
        var run = await h.RunAsync(plan, snapshot);
        Assert.Equal(RunStatus.ReviewRequired, run.Status);
        Assert.Single(h.Graph.Writes);
        var first = run.Results[0];
        Assert.Equal(ResultStatus.Completed, first.Status);
        Assert.Equal(WriteAcceptance.Accepted, first.WriteAcceptance);
        Assert.Equal(ConfigurationVerification.Unknown, first.Configuration);
        Assert.NotNull(first.ObjectId);
        Assert.NotNull(first.WrittenAt);
        Assert.Single(h.Evidence.LoadMappings(TestData.TenantA).ByControl);
        Assert.Equal(ResultStatus.NotRun, run.Results[1].Status);
        Assert.NotNull(run.AfterSnapshotId);
    }

    [Fact]
    public async Task Deviation_change_during_run_stops_before_another_write()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001", "CA-003");
        h.Graph.BeforeWrite = (_, _) =>
        {
            h.Evidence.SaveDeviations(TestData.TenantA, new[] { new Deviation { TenantId = TestData.TenantA, ControlId = "CA-003", Reason = "Excluded during run" } });
            return Task.CompletedTask;
        };
        var run = await h.RunAsync(plan, snapshot);
        Assert.Single(h.Graph.Writes);
        Assert.Equal(RunStatus.ReviewRequired, run.Status);
        Assert.Equal(WriteAcceptance.NotAttempted, run.Results[1].WriteAcceptance);
        Assert.Equal(ConfigurationVerification.NotRun, run.Results[1].Configuration);
    }

    [Fact]
    public async Task Second_deployment_creates_no_duplicates()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001");
        await h.RunAsync(plan, snapshot);
        Assert.Single(h.Graph.Writes);

        var (snapshot2, plan2) = await h.CaptureAndPlanAsync("CA-001");
        Assert.Equal(PlanAction.NoChange, plan2.Rows[0].Action);
        Assert.Throws<PlanValidationException>(() => DeploymentPlanner.Validate(plan2, new PlanValidationContext
        {
            Profile = h.Profile, Standard = h.Standard, Snapshot = snapshot2, Mappings = h.Evidence.LoadMappings(TestData.TenantA), Session = h.Session, AcknowledgedSnapshotId = snapshot2.Id, Now = h.Clock.UtcNow
        }));
        Assert.Single(h.Graph.Collection("/identity/conditionalAccess/policies"));
    }

    [Fact]
    public async Task Ambiguous_write_failure_stops_the_run_without_mapping()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001", "CA-003");
        h.Graph.ThrowOnWrite = new AmbiguousWriteException("Simulated timeout", null);
        var run = await h.RunAsync(plan, snapshot);

        Assert.Equal(RunStatus.ReviewRequired, run.Status);
        Assert.Single(h.Graph.Writes);
        Assert.Equal(ResultStatus.Error, run.Results[0].Status);
        Assert.Equal(ConfigurationVerification.Unknown, run.Results[0].Configuration);
        Assert.Equal(ResultStatus.NotRun, run.Results[1].Status);
        Assert.Empty(h.Evidence.LoadMappings(TestData.TenantA).ByControl);
        Assert.NotNull(run.AfterSnapshotId);
        await Assert.ThrowsAsync<PlanValidationException>(() => h.RunAsync(plan, snapshot));
        Assert.Single(h.Graph.Writes);
    }

    [Fact]
    public async Task Fresh_plan_cannot_replay_an_unresolved_create_even_when_new_capture_is_empty()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001");
        h.Graph.ThrowOnWrite = new AmbiguousWriteException("Simulated unknown acceptance", null);
        var previous = await h.RunAsync(plan, snapshot);
        h.Graph.ThrowOnWrite = null;
        var (freshSnapshot, freshPlan) = await h.CaptureAndPlanAsync("CA-001");
        Assert.NotEqual(plan.Id, freshPlan.Id);
        Assert.Equal(PlanAction.Create, freshPlan.Rows.Single().Action);
        var ex = await Assert.ThrowsAsync<PlanValidationException>(() => h.RunAsync(freshPlan, freshSnapshot));
        Assert.Contains(previous.Id, ex.Message, StringComparison.Ordinal);
        Assert.Single(h.Graph.Writes);

        // The unresolved control is isolated; a separately reviewed unrelated candidate remains possible.
        var (otherSnapshot, otherPlan) = await h.CaptureAndPlanAsync("CA-003");
        var other = await h.RunAsync(otherPlan, otherSnapshot);
        Assert.Equal(RunStatus.Completed, other.Status);
        Assert.Equal(2, h.Graph.Writes.Count);
    }

    [Fact]
    public async Task Accepted_write_without_an_object_id_blocks_fresh_plans_for_the_control()
    {
        using var h = new Harness();
        var previous = new DeploymentRun
        {
            Id = Guid.NewGuid().ToString(), PlanId = Guid.NewGuid().ToString(), TenantId = TestData.TenantA,
            StartedAt = Timestamps.Format(h.Clock.UtcNow), Status = RunStatus.ReviewRequired,
            Results = new List<RunResult>
            {
                new() { ControlId = "CA-001", PlannedAction = nameof(PlanAction.Create), Status = ResultStatus.Error,
                    WriteAcceptance = WriteAcceptance.Accepted, Configuration = ConfigurationVerification.Unknown }
            }
        };
        h.Evidence.SaveRun(previous);
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001");
        var ex = await Assert.ThrowsAsync<PlanValidationException>(() => h.RunAsync(plan, snapshot));
        Assert.Contains(previous.Id, ex.Message, StringComparison.Ordinal);
        Assert.Empty(h.Graph.Writes);
    }

    [Fact]
    public async Task Malformed_previous_run_evidence_cannot_hide_an_ambiguous_attempt()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001");
        h.Graph.ThrowOnWrite = new AmbiguousWriteException("Unknown outcome", null);
        var run = await h.RunAsync(plan, snapshot);
        File.WriteAllText(h.Evidence.RunFile(run), "{incomplete");
        var (nextSnapshot, nextPlan) = await h.CaptureAndPlanAsync("CA-001");
        await Assert.ThrowsAsync<ConfigurationException>(() => h.RunAsync(nextPlan, nextSnapshot));
        Assert.Single(h.Graph.Writes);
    }

    [Fact]
    public async Task Name_collision_after_planning_blocks_the_write()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001");
        h.Graph.Add("/identity/conditionalAccess/policies", TestData.ConditionalAccessPolicy("x1", "BDIT - CA-001 - Require MFA", "enabled", Array.Empty<string>(), withLocation: false));
        var run = await h.RunAsync(plan, snapshot);
        Assert.Equal(RunStatus.ReviewRequired, run.Status);
        Assert.Empty(h.Graph.Writes);
        Assert.Contains("appeared", run.Results[0].Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_only_graph_session_cannot_execute()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001");
        h.Graph.Mode = SessionMode.Assessment;
        await Assert.ThrowsAsync<WriteDeniedException>(() => h.RunAsync(plan, snapshot));
        Assert.Empty(h.Graph.Writes);
    }

    [Fact]
    public async Task Readback_mismatch_marks_run_for_review_but_keeps_the_object_mapped()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001");
        h.Graph.MutateReadback = o => { o["grantControls"]!["builtInControls"] = new JsonArray("block"); return o; };
        var run = await h.RunAsync(plan, snapshot);
        Assert.Equal(RunStatus.ReviewRequired, run.Status);
        Assert.Equal(ConfigurationVerification.Unknown, run.Results[0].Configuration);
        Assert.Equal(ResultStatus.Completed, run.Results[0].Status);
        Assert.Single(h.Evidence.LoadMappings(TestData.TenantA).ByControl);
    }

    [Fact]
    public async Task Stop_request_finishes_the_current_write_and_skips_the_rest()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001", "CA-003");
        var control = new DeploymentControl();
        h.Graph.BeforeWrite = (_, _) => { control.Stop(); return Task.CompletedTask; };
        var run = await h.RunAsync(plan, snapshot, control);
        Assert.Equal(RunStatus.Stopped, run.Status);
        Assert.Single(h.Graph.Writes);
        Assert.Equal(ResultStatus.Completed, run.Results[0].Status);
        Assert.Equal(ResultStatus.NotRun, run.Results[1].Status);
        Assert.NotNull(run.AfterSnapshotId);
    }

    [Fact]
    public async Task Shutdown_waits_for_the_in_flight_write()
    {
        using var h = new Harness();
        var (snapshot, plan) = await h.CaptureAndPlanAsync("CA-001", "CA-003");
        var gate = new TaskCompletionSource();
        h.Graph.BeforeWrite = async (_, _) => await gate.Task;
        var control = new DeploymentControl();
        var runTask = h.RunAsync(plan, snapshot, control);
        await Task.Delay(50);
        Assert.True(h.Executor.IsRunning);

        var waiter = h.Executor.WaitForCompletionAsync(control);
        Assert.False(waiter.IsCompleted);
        gate.SetResult();
        await waiter;
        var run = await runTask;
        Assert.False(h.Executor.IsRunning);
        Assert.Single(h.Graph.Writes);
        Assert.Equal(RunStatus.Stopped, run.Status);
        Assert.Equal(ResultStatus.Completed, run.Results[0].Status);
    }

    [Fact]
    public void Interrupted_runs_are_marked_honestly_at_start_up()
    {
        using var h = new Harness();
        var run = new DeploymentRun { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, StartedAt = "2026-09-11T09:00:00.000Z", Status = RunStatus.Running,
            Results = new List<RunResult> { new() { ControlId = "CA-001", Status = ResultStatus.InProgress }, new() { ControlId = "CA-003", Status = ResultStatus.Pending } } };
        h.Evidence.SaveRun(run);
        Assert.Equal(1, h.Evidence.MarkInterruptedRuns(TestData.TenantA));
        var loaded = h.Evidence.LoadRun(TestData.TenantA, run.Id)!;
        Assert.Equal(RunStatus.Interrupted, loaded.Status);
        Assert.Equal(ResultStatus.Error, loaded.Results[0].Status);
        Assert.Equal(ResultStatus.NotRun, loaded.Results[1].Status);
        Assert.Equal(0, h.Evidence.MarkInterruptedRuns(TestData.TenantA));
    }
}
