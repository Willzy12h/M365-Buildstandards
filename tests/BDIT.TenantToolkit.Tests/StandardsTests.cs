using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;

namespace BDIT.TenantToolkit.Tests;

public class StandardsTests
{
    private static JsonObject Control(JsonObject root, string id) =>
        root["controls"]!.AsArray().OfType<JsonObject>().First(c => c["id"]!.GetValue<string>() == id);

    private static string Mutate(Action<JsonObject> change)
    {
        var root = ToolkitJson.ParseObject(TestData.StandardJson);
        change(root);
        return root.ToJsonString(ToolkitJson.Options);
    }

    [Fact]
    public void Valid_standard_parses_with_recipes_and_manual_controls()
    {
        var standard = TestData.Standard();
        Assert.Equal("test.1", standard.Release);
        Assert.Equal(4, standard.Controls.Count);
        Assert.Equal(3, standard.Controls.Count(c => c.HasRecipe));
        Assert.Contains("Policy.ReadWrite.ConditionalAccess", standard.WriteScopes());
        Assert.True(standard.Collections["settingsCatalogue"].ApiVersion == Core.Models.GraphApi.Beta);
    }

    [Fact]
    public void Unsupported_schema_version_is_rejected()
    {
        var json = Mutate(r => r["schemaVersion"] = 2);
        var ex = Assert.Throws<ConfigurationException>(() => StandardsLoader.Parse(json, "x.json"));
        Assert.Contains("schema version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_control_ids_are_rejected()
    {
        var json = Mutate(r => Control(r, "CA-003")["id"] = "CA-001");
        Assert.Throws<ConfigurationException>(() => StandardsLoader.Parse(json, "x.json"));
    }

    [Fact]
    public void Conditional_access_recipe_cannot_request_enabled_state()
    {
        var json = Mutate(r => Control(r, "CA-001")["payload"]!["state"] = "enabled");
        var ex = Assert.Throws<ConfigurationException>(() => StandardsLoader.Parse(json, "x.json"));
        Assert.Contains("disabled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Conditional_access_recipe_must_declare_disabled_safe_state()
    {
        var json = Mutate(r => Control(r, "CA-001")["safeDeployment"]!["state"] = "reportOnly");
        Assert.Throws<ConfigurationException>(() => StandardsLoader.Parse(json, "x.json"));
    }

    [Fact]
    public void Payload_with_assignments_or_manual_control_with_payload_is_rejected()
    {
        var withAssignments = Mutate(r => Control(r, "CMP-WIN-001")["payload"]!["assignments"] = new JsonArray());
        Assert.Throws<ConfigurationException>(() => StandardsLoader.Parse(withAssignments, "x.json"));
        var manualWithPayload = Mutate(r => Control(r, "ID-001")["payload"] = new JsonObject { ["displayName"] = "x" });
        Assert.Throws<ConfigurationException>(() => StandardsLoader.Parse(manualWithPayload, "x.json"));
        var unknownCollection = Mutate(r => Control(r, "CA-001")["collection"] = "nope");
        Assert.Throws<ConfigurationException>(() => StandardsLoader.Parse(unknownCollection, "x.json"));
        var versionInPath = Mutate(r => r["collections"]!["groups"]!["path"] = "/v1.0/groups");
        Assert.Throws<ConfigurationException>(() => StandardsLoader.Parse(versionInPath, "x.json"));
    }

    [Fact]
    public void Missing_manifest_blocks_load()
    {
        using var root = new TempRoot();
        root.WriteStandard("test.json", TestData.StandardJson);
        var loader = new StandardsLoader(root.Paths, NullLog.Instance);
        Assert.Throws<IntegrityException>(() => loader.Load("test.json"));
    }

    [Fact]
    public void Modified_standard_blocks_load_and_intact_standard_loads()
    {
        using var root = new TempRoot();
        var file = root.WriteStandard("test.json", TestData.StandardJson);
        root.WriteManifest();
        var loader = new StandardsLoader(root.Paths, NullLog.Instance);
        var loaded = loader.Load("test.json");
        Assert.Equal(64, loaded.IntegrityDigest.Length);

        File.AppendAllText(file, "\n");
        var ex = Assert.Throws<IntegrityException>(() => loader.Load("test.json"));
        Assert.Contains("modified", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unlisted_file_blocks_load()
    {
        using var root = new TempRoot();
        root.WriteStandard("test.json", TestData.StandardJson);
        root.WriteManifest();
        root.WriteStandard("later.json", Mutate(r => r["release"] = "test.2"));
        var loader = new StandardsLoader(root.Paths, NullLog.Instance);
        Assert.Throws<IntegrityException>(() => loader.Load("later.json"));
        Assert.Equal(2, loader.ListReleases().Count);
    }

    [Fact]
    public void Shipped_standard_release_is_valid()
    {
        var repoStandards = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards"));
        if (!Directory.Exists(repoStandards)) return;
        foreach (var file in Directory.EnumerateFiles(repoStandards, "*.json").Where(f => !f.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            var catalogue = StandardsLoader.Parse(File.ReadAllText(file), Path.GetFileName(file));
            Assert.NotEmpty(catalogue.Controls);
            foreach (var control in catalogue.Controls.Where(c => c.Collection == "conditionalAccess" && c.HasRecipe))
                Assert.Equal("disabled", control.Payload!["state"]!.GetValue<string>());
        }
    }

    /// <summary>
    /// From 2026.09.7 the three controls that are changed only through reviewed tenant actions are assessed from the
    /// collections the toolkit already captures. They stay manual-mode (no recipe, no Plan → Deploy path) but declare
    /// equivalence signals, so a report shows the tenant's actual state instead of "requires manual review".
    /// </summary>
    [Fact]
    public void Latest_shipped_standard_assesses_reviewed_action_controls_from_evidence()
    {
        var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards", "2026.09.7.json"));
        if (!File.Exists(file)) return;
        var catalogue = StandardsLoader.Parse(File.ReadAllText(file), Path.GetFileName(file));
        Assert.Equal("2026.09.7", catalogue.Release);
        Assert.Equal(45, catalogue.Controls.Count);
        Assert.Equal(37, catalogue.Controls.Count(c => c.HasRecipe));
        foreach (var id in new[] { "ID-002", "ENR-001", "CMP-001" })
        {
            var control = catalogue.FindControl(id)!;
            Assert.Equal(AssessmentMode.Manual, control.Assessment.Mode);
            Assert.Null(control.Payload);
            Assert.NotNull(control.Equivalence);
            Assert.NotEmpty(control.Equivalence!.Required);
            Assert.True(catalogue.Collections.ContainsKey(control.Equivalence.Collection ?? control.Collection!));
        }
        Assert.Contains(catalogue.FindControl("ID-002")!.Equivalence!.Signals, s => s.Path.Contains("[id=Sms]", StringComparison.Ordinal));
        // Manual-by-nature controls carry no equivalence claim: nothing readable proves them.
        foreach (var id in new[] { "ID-001", "ID-003", "ENR-005", "ENR-006", "UPD-001" })
            Assert.Null(catalogue.FindControl(id)!.Equivalence);
    }

    /// <summary>
    /// From 2026.09.8 the directory objects every other control depends on are provisioned through the same
    /// Plan → Deploy path as a policy: empty security groups and an untrusted office named location. Administrator
    /// access is assessed from directory role membership instead of being left to an engineer's cross-reference.
    /// </summary>
    [Fact]
    public void Latest_shipped_standard_provisions_directory_prerequisites()
    {
        var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards", "2026.09.8.json"));
        if (!File.Exists(file)) return;
        var catalogue = StandardsLoader.Parse(File.ReadAllText(file), Path.GetFileName(file));
        Assert.Equal("2026.09.8", catalogue.Release);
        Assert.Equal(53, catalogue.Controls.Count);
        Assert.Equal(45, catalogue.Controls.Count(c => c.HasRecipe));

        // Both prerequisite collections must be writable, or every prerequisite control plans as Manual.
        Assert.True(catalogue.Collections["groups"].Writable);
        Assert.True(catalogue.Collections["namedLocations"].Writable);

        // The planner resolves reviewed client inputs before the guard sees a payload, so the named location's
        // ipRanges placeholder is resolved here the same way; a raw template is never written.
        var inputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["officeIpRanges"] = new JsonArray(new JsonObject
            {
                ["@odata.type"] = "#microsoft.graph.iPv4CidrRange", ["cidrAddress"] = "203.0.113.0/24"
            })
        };
        foreach (var control in catalogue.Controls.Where(c => c.Id.StartsWith("PRE-", StringComparison.Ordinal)))
        {
            Assert.True(control.HasRecipe);
            var def = catalogue.Collections[control.Collection!];
            var payload = (JsonObject)CanonicalJson.Resolve(control.Payload, inputs)!;
            WritePayloadGuard.Assert(def, payload);
        }

        var groups = catalogue.Controls.Where(c => c.Collection == "groups").ToList();
        Assert.Equal(7, groups.Count);
        foreach (var group in groups)
        {
            Assert.Equal("none", group.SafeDeployment.Assignment);
            Assert.Equal(group.Name, group.Payload!["displayName"]!.GetValue<string>());
        }
        Assert.Equal("#microsoft.graph.ipNamedLocation", catalogue.FindControl("PRE-008")!.Payload!["@odata.type"]!.GetValue<string>());
        Assert.False(catalogue.FindControl("PRE-008")!.Payload!["isTrusted"]!.GetValue<bool>());

        var adminAccess = catalogue.FindControl("ID-003")!;
        Assert.Equal("directoryRoles", adminAccess.Equivalence!.Collection);
        Assert.True(catalogue.Collections.ContainsKey("directoryRoles"));
        Assert.Contains(adminAccess.Equivalence.Signals, s => s.Operator == SignalOperator.AtMost);
        Assert.Contains(adminAccess.Equivalence.Signals, s => s.Operator == SignalOperator.AtLeast);
    }

    /// <summary>
    /// From 2026.09.9 the standard targets the built-in All users and All devices populations and creates groups only
    /// where something has to be named: the two exclusion groups, the unenrolled-mobile population and the pilot set.
    /// Reviewable inputs carry a dated default so a candidate is created with a warning instead of being blocked.
    /// </summary>
    [Fact]
    public void Latest_shipped_standard_targets_built_in_populations_and_defaults_reviewable_inputs()
    {
        var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards", "2026.09.9.json"));
        if (!File.Exists(file)) return;
        var catalogue = StandardsLoader.Parse(File.ReadAllText(file), Path.GetFileName(file));
        Assert.Equal("2026.09.9", catalogue.Release);
        Assert.Equal(50, catalogue.Controls.Count);
        Assert.Equal(4, catalogue.Controls.Count(c => c.Collection == "groups"));

        // Every policy targets a built-in population and names only the group it excludes.
        foreach (var control in catalogue.Controls.Where(c => c.HasRecipe && c.Collection != "groups" && c.Collection != "namedLocations"))
            Assert.Contains("built-in", control.ExpectedProduction.Assignment, StringComparison.OrdinalIgnoreCase);

        // Identity inputs must never carry a default: a wrong exclusion is how a tenant locks itself out.
        foreach (var key in new[] { "emergencyAccountIds", "officeLocationId", "mamGroupId", "officeIpRanges" })
            Assert.False(catalogue.Parameters.Single(p => p.Key == key).HasDefault, key + " must not default");

        var android = catalogue.Parameters.Single(p => p.Key == "androidMinimumVersion");
        Assert.Equal("14", android.Default!.GetValue<string>());
        Assert.False(android.IsStale(DateTimeOffset.Parse(android.ReviewedOn).AddDays(30)));
        Assert.True(android.IsStale(DateTimeOffset.Parse(android.ReviewedOn).AddDays(120)));

        // Reviewed minimum operating system versions, recorded with the release that checked them.
        Assert.Equal("10.0.26200.0", catalogue.FindControl("CMP-WIN-001")!.Payload!["osMinimumVersion"]!.GetValue<string>());
        Assert.Equal("26.7", catalogue.FindControl("CMP-IOS-001")!.Payload!["osMinimumVersion"]!.GetValue<string>());
    }
}
