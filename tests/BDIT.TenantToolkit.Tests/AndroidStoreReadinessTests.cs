using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Collection;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Execution;
using BDIT.TenantToolkit.Engine.Planning;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class AndroidStoreReadinessTests
{
    private const string PlayPath = "/deviceManagement/androidManagedStoreAccountEnterpriseSettings";
    private const string ControlId = "APP-AND-001";

    private static StandardCatalogue Standard()
    {
        var standard = TestData.Standard();
        var release = Release20260912Tests.Standard();
        var app = release.FindControl(ControlId)!;
        standard.SchemaVersion = 5;
        standard.Controls.Add(app);
        standard.Collections[app.Collection!] = release.Collections[app.Collection!];
        standard.Collections["googlePlay"] = release.Collections["googlePlay"];
        standard.Parameters.AddRange(release.Parameters.Where(p => p.Key is "andStoreUrl1" or "androidPackageId1"));
        return standard;
    }

    private static TenantProfile Profile()
    {
        var profile = TestData.Profile();
        profile.Parameters.PolicyInputs = new()
        {
            ["andStoreUrl1"] = "https://play.google.com/store/apps/details?id=example.synthetic",
            ["androidPackageId1"] = "example.synthetic"
        };
        return profile;
    }

    private static JsonObject Binding(string state) => state switch
    {
        "missing-field" => new JsonObject(),
        "null" => new JsonObject { ["bindStatus"] = null },
        "object" => new JsonObject { ["bindStatus"] = new JsonObject() },
        "boolean" => new JsonObject { ["bindStatus"] = true },
        _ => new JsonObject { ["bindStatus"] = state }
    };

    private static TenantSnapshot Snapshot(StandardCatalogue standard, string state = "boundAndValidated")
    {
        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections["googlePlay"].Items.Add(Binding(state));
        snapshot.Collections["googlePlay"].Count = 1;
        return snapshot;
    }

    private static DeploymentPlan Plan(StandardCatalogue standard, TenantSnapshot snapshot, TenantProfile profile) =>
        new DeploymentPlanner(new FixedClock(), "test").Build(new PlanRequest
        {
            Standard = standard, Snapshot = snapshot, Profile = profile, Session = TestData.Session(),
            Mappings = TestData.Mappings(), Deviations = Array.Empty<Deviation>(), SelectedControlIds = new[] { ControlId }
        });

    [Theory]
    [InlineData("missing")]
    [InlineData("error")]
    [InlineData("not-attempted")]
    [InlineData("incomplete")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    public void Captured_binding_must_be_complete_and_unique(string fault)
    {
        var standard = Standard();
        var snapshot = Snapshot(standard);
        var capture = snapshot.Collections["googlePlay"];
        switch (fault)
        {
            case "missing": snapshot.Collections.Remove("googlePlay"); break;
            case "error": capture.Status = CaptureStatus.Error; break;
            case "not-attempted": capture.Status = CaptureStatus.NotAttempted; break;
            case "incomplete": capture.DetailIncomplete = true; break;
            case "empty": capture.Items.Clear(); capture.Count = 0; break;
            case "duplicate": capture.Items.Add(Binding("boundAndValidated")); capture.Count = 2; break;
        }
        Assert.Contains("Managed Google Play", DeviceCandidateReadiness.Problem(standard.FindControl(ControlId)!.Payload!, standard, snapshot));
    }

    [Theory]
    [InlineData("notBound")]
    [InlineData("bound")]
    [InlineData("unknownFutureValue")]
    [InlineData("missing-field")]
    [InlineData("null")]
    [InlineData("object")]
    [InlineData("boolean")]
    public void Planner_refuses_unvalidated_or_malformed_captured_binding(string state)
    {
        var standard = Standard();
        var row = Assert.Single(Plan(standard, Snapshot(standard, state), Profile()).Rows);
        Assert.Equal(PlanAction.Blocked, row.Action);
        Assert.Contains("Managed Google Play", row.Reason);
    }

    [Theory]
    [InlineData("notBound")]
    [InlineData("unknownFutureValue")]
    [InlineData("missing-field")]
    [InlineData("not-found")]
    [InlineData("boundAndValidated")]
    public async Task Executor_rechecks_binding_after_preview_before_sending_an_app_write(string state)
    {
        using var root = new TempRoot();
        var standard = Standard(); var snapshot = Snapshot(standard); var profile = Profile();
        var plan = Plan(standard, snapshot, profile);
        Assert.Equal(PlanAction.Create, Assert.Single(plan.Rows).Action);
        var evidence = new EvidenceStore(root.Paths, NullLog.Instance);
        evidence.SaveSnapshot(snapshot);
        var graph = new FakeGraphClient(standard);
        foreach (var (key, capture) in snapshot.Collections.Where(c => !standard.Collections[c.Key].Singleton))
            foreach (var item in capture.Items) graph.Add(standard.Collections[key].BasePath, (JsonObject)item.DeepClone());
        if (state != "not-found") graph.SetSingleton(PlayPath, Binding(state));
        var clock = new FixedClock();
        var executor = new DeploymentExecutor(evidence, new TenantCollector(NullLog.Instance, clock, "test"), NullLog.Instance, clock, "test");
        var run = await executor.StartAsync(new ExecutionRequest
        {
            Standard = standard, Snapshot = snapshot, Profile = profile, Session = TestData.Session(), Graph = graph,
            Mappings = TestData.Mappings(), Plan = plan, AcknowledgedSnapshotId = snapshot.Id, TypedTenant = TestData.TenantA
        }, new(), null);
        Assert.Contains((GraphApi.Beta, PlayPath + "?$select=id,bindStatus,lastAppSyncDateTime,lastAppSyncStatus"), graph.VersionedReads);
        var result = Assert.Single(run.Results);
        if (state == "boundAndValidated")
        {
            Assert.Equal(RunStatus.Completed, run.Status);
            Assert.Equal(ConfigurationVerification.Pass, result.Configuration);
            Assert.False(Assert.Single(graph.Writes).Payload.ContainsKey("assignments"));
        }
        else
        {
            Assert.Equal(RunStatus.ReviewRequired, run.Status);
            Assert.Equal(WriteAcceptance.NotAttempted, result.WriteAcceptance);
            Assert.Empty(graph.Writes);
            if (state != "not-found") Assert.Contains("Managed Google Play", result.Reason);
        }
    }
}
