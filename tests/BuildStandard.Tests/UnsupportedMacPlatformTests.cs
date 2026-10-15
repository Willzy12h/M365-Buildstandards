using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Standards;
using Xunit;

namespace BuildStandard.Tests;

public sealed class UnsupportedMacPlatformTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [Fact]
    public void Default_compliant_desktop_recipe_targets_only_the_supported_Windows_platform()
    {
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "config/toolkit.settings.json")))!;
        var release = config["defaultStandardRelease"]!.GetValue<string>();
        var standard = StandardsLoader.Parse(File.ReadAllText(Path.Combine(Root, "standards", release + ".json")), release + ".json");
        var control = standard.FindControl("CA-005")!;
        Assert.Equal(new[] { "windows" }, control.Payload!["conditions"]!["platforms"]!["includePlatforms"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Contains("CMP-WIN-001", control.Dependencies);
        Assert.Equal("disabled", control.Payload["state"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("#microsoft.graph.macOSCompliancePolicy")]
    [InlineData("#microsoft.graph.macOSGeneralDeviceConfiguration")]
    [InlineData("#microsoft.graph.macOSLobApp")]
    public void Mac_device_recipes_are_refused_even_without_assignments(string type)
    {
        var definition = new CollectionDefinition { Path = "/deviceManagement/deviceCompliancePolicies", Write = "DeviceManagementConfiguration.ReadWrite.All" };
        var payload = new JsonObject { ["@odata.type"] = type, ["displayName"] = "Synthetic unsupported candidate" };
        var error = Assert.Throws<SafetyViolationException>(() => WritePayloadGuard.Assert(definition, payload));
        Assert.Contains("macOS", error.Message);
    }

    [Theory]
    [InlineData("macOS")][InlineData("all")]
    public void Compliance_requirements_cannot_cover_unsupported_macOS(string platform)
    {
        var definition = new CollectionDefinition { Path = "/identity/conditionalAccess/policies" };
        var payload = JsonNode.Parse("""{"state":"disabled","conditions":{"platforms":{"includePlatforms":["windows"]}},"grantControls":{"builtInControls":["compliantDevice"]}}""")!.AsObject();
        payload["conditions"]!["platforms"]!["includePlatforms"] = new JsonArray(platform);
        var error = Assert.Throws<SafetyViolationException>(() => WritePayloadGuard.Assert(definition, payload));
        Assert.Contains("macOS", error.Message);
    }
    [Fact]
    public void Successor_lineage_preserves_history_and_marks_only_CA005_as_changed()
    {
        var directory = Path.Combine(Root, "standards");
        var manifest = StandardsManifest.Load(directory);
        Assert.Equal("124a033454385b262960db2a6f61392ee3e64ad8abb1a6f0d8276db29e21302f", manifest.Verify(directory, "2026.09.30.json"));
        Assert.Equal(64, manifest.Verify(directory, "2026.10.15.json").Length);
        var lineage = ReleaseLineage.Load(directory, manifest, "2026.10.15")!;
        var relation = Assert.Single(Assert.Single(lineage.Sources).Relations);
        Assert.Equal("CA-005", relation.SourceControl); Assert.Equal("Changed", relation.Relation);
        var old = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "2026.09.30.json")))!;
        var current = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "2026.10.15.json")))!;
        var oldControls = old["controls"]!.AsArray().ToDictionary(x => x!["id"]!.ToString());
        Assert.Equal(oldControls.Count, current["controls"]!.AsArray().Count);
        foreach (var control in current["controls"]!.AsArray().Where(x => x!["id"]!.ToString() != "CA-005"))
            Assert.True(JsonNode.DeepEquals(oldControls[control!["id"]!.ToString()], control));
        Assert.True(JsonNode.DeepEquals(old["collections"], current["collections"]));
        Assert.True(JsonNode.DeepEquals(old["parameters"], current["parameters"]));
    }

    [Fact]
    public void Windows_compliance_and_all_platform_MFA_are_preserved()
    {
        var definition = new CollectionDefinition { Path = "/identity/conditionalAccess/policies" };
        var payload = JsonNode.Parse("""{"state":"disabled","conditions":{"platforms":{"includePlatforms":["windows"]}},"grantControls":{"builtInControls":["compliantDevice"]}}""")!.AsObject();
        WritePayloadGuard.Assert(definition, payload);
        payload["conditions"]!["platforms"]!["includePlatforms"] = new JsonArray("all");
        payload["grantControls"]!["builtInControls"] = new JsonArray("mfa");
        WritePayloadGuard.Assert(definition, payload);
        payload["grantControls"]!["builtInControls"] = new JsonArray("compliantDevice");
        payload["conditions"]!["platforms"]!["excludePlatforms"] = new JsonArray("macOS");
        WritePayloadGuard.Assert(definition, payload);
    }

}
