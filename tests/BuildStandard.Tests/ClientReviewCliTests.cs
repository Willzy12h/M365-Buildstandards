using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Workflow;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ClientReviewCliTests
{
    [Fact] public async Task Clients_uses_the_shared_offline_projection_and_does_not_write_evidence()
    {
        using var root=new TempRoot();var store=new EvidenceStore(root.Paths,NullLog.Instance);var profile=TestData.Profile();store.SaveProfiles([profile]);
        store.SaveRun(new DeploymentRun{Id=Guid.NewGuid().ToString(),TenantId=TestData.TenantA,StartedAt="2026-09-11T10:00:00Z",Results=[new RunResult{ControlId="CA-001",WriteAcceptance=WriteAcceptance.Unknown}]});
        var before=Directory.GetFiles(root.Root,"*",SearchOption.AllDirectories).Order().ToArray();
        var expected=ClientReviewProjection.Project(profile,store.LoadClientReviewEvidence(TestData.TenantA),DateTimeOffset.UtcNow);
        var result=await RegisteredReportCliTests.Run("clients","--root",root.Root);Assert.True(result.Exit==0,result.Output+result.Error);
        var doc=ToolkitJson.ParseObject(result.Output);var row=Assert.Single((System.Text.Json.Nodes.JsonArray)doc["clients"]!);
        Assert.Equal(expected.UnresolvedWrites,row!["unresolvedWrites"]!.GetValue<int>());Assert.Equal(expected.WriteHistoryKnown,row["writeHistoryKnown"]!.GetValue<bool>());
        Assert.Equal(before,Directory.GetFiles(root.Root,"*",SearchOption.AllDirectories).Order().ToArray());
    }
    [Fact] public async Task Review_exports_the_same_one_client_pack_without_authentication()
    {
        using var root=new TempRoot();new EvidenceStore(root.Paths,NullLog.Instance).SaveProfiles([TestData.Profile()]);
        var result=await RegisteredReportCliTests.Run("review","--tenant",TestData.TenantA,"--output",root.Paths.ReportsDirectory,"--root",root.Root);
        Assert.True(result.Exit==0,result.Output+result.Error);var json=ToolkitJson.ParseObject(result.Output);
        var folder=json["directory"]!.GetValue<string>();Assert.True(File.Exists(Path.Combine(folder,"engineer.html")));
        Assert.Contains(TestData.TenantA,File.ReadAllText(Path.Combine(folder,"manifest.json")));Assert.False(Directory.Exists(root.Paths.TenantDirectory(TestData.TenantA)));
    }
    [Fact] public async Task Unknown_client_is_refused_without_creating_a_review()
    {
        using var root=new TempRoot();var result=await RegisteredReportCliTests.Run("review","--tenant",TestData.TenantB,"--output",root.Paths.ReportsDirectory,"--root",root.Root);
        Assert.Equal(2,result.Exit);Assert.Contains("saved client",result.Error,StringComparison.OrdinalIgnoreCase);Assert.Empty(Directory.GetDirectories(root.Paths.ReportsDirectory));
    }
    [Theory] [InlineData("--tenant")] [InlineData("--connect")]
    public async Task Clients_rejects_unsupported_options(string option)
    {
        using var root=new TempRoot();var result=await RegisteredReportCliTests.Run("clients",option,TestData.TenantA,"--root",root.Root);
        Assert.Equal(2,result.Exit);Assert.Contains("Unsupported",result.Error);
    }
}
