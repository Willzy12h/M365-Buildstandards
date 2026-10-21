using System.Text.Json.Nodes;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Standards;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CandidateImportPreparationTests
{
    [Fact]public void Preparation_reuses_existing_sanitisation_without_adopting_a_foreign_reference()
    {
        var baseline = DevicePolicyImportTests.Shipped(); var before = ToolkitJson.Serialize(baseline);
        var source = baseline.FindControl("CFG-WIN-002")!.Payload!.DeepClone().AsObject();
        source["id"] = TestData.TenantB; source["assignments"] = new JsonArray(new JsonObject { ["id"] = TestData.Office });
        source["roleScopeTagIds"] = new JsonArray("1");
        source["omaSettings"]![0]!["displayName"] = TestData.Office;
        var json = source.ToJsonString(); var prepared = PolicyImporter.Prepare(baseline, "CFG-WIN-002", json, "CFG - Candidate - Windows");
        Assert.Null(prepared.Payload["id"]); Assert.Null(prepared.Payload["assignments"]); Assert.Null(prepared.Payload["roleScopeTagIds"]);
        Assert.Equal(TestData.Office, prepared.Payload["omaSettings"]![0]!["displayName"]!.GetValue<string>());
        Assert.Contains("assignments", prepared.RemovedProperties); Assert.Equal(before, ToolkitJson.Serialize(baseline)); Assert.Equal(json, source.ToJsonString());
    }
    [Fact]public void Existing_import_keeps_the_same_prepared_payload_and_removed_metadata()
    {
        var baseline = DevicePolicyImportTests.Shipped(); var source = baseline.FindControl("CFG-WIN-002")!.Payload!.DeepClone().AsObject();
        source["id"] = TestData.TenantB; source["assignments"] = new JsonArray(); var json = source.ToJsonString();
        var prepared = PolicyImporter.Prepare(baseline, "CFG-WIN-002", json, "CFG - Candidate - Windows");
        var imported = PolicyImporter.Import(baseline, "CFG-WIN-002", json, "CFG - Candidate - Windows");
        Assert.Equal(CanonicalJson.Serialize(imported.Standard.FindControl("CFG-WIN-002")!.Payload), CanonicalJson.Serialize(prepared.Payload));
        Assert.Equal(imported.RemovedProperties, prepared.RemovedProperties);
    }
}
