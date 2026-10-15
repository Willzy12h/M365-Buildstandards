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
}
