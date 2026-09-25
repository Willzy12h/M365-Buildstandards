using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Collection;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Execution;
using BDIT.TenantToolkit.Engine.Planning;
using BDIT.TenantToolkit.Engine.Standards;
using BDIT.TenantToolkit.Graph;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class Release20260912Tests
{
    internal static StandardCatalogue Standard() => StandardsLoader.Parse(File.ReadAllText(Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards", "2026.09.12.json"))), "2026.09.12.json");

    [Fact]
    public void Checked_in_manifest_matches_the_new_release_bytes()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards"));
        Assert.Equal(64, StandardsManifest.Load(dir).Verify(dir, "2026.09.12.json").Length);
    }

    [Theory]
    [InlineData("10.1.0.0/16")][InlineData("172.16.0.0/12")][InlineData("192.168.1.0/24")]
    [InlineData("127.0.0.1/32")][InlineData("169.254.1.0/24")][InlineData("100.64.0.0/10")]
    [InlineData("192.0.2.0/24")][InlineData("198.51.100.0/24")][InlineData("203.0.113.0/24")]
    [InlineData("224.0.0.0/4")][InlineData("0.0.0.0/32")][InlineData("0.0.0.0/0")]
    [InlineData("::/0")][InlineData("::/128")][InlineData("::1/128")][InlineData("fe80::/64")]
    [InlineData("fc00::/7")][InlineData("ff00::/8")][InlineData("2001:db8::/32")][InlineData("3fff::/20")]
    [InlineData("8.0.0.0/6")][InlineData("2000::/4")][InlineData("::ffff:8.8.8.8/128")]
    [InlineData("8.8.8.8")][InlineData("8.8.8.8/33")][InlineData("8.8.8.8/-1")]
    [InlineData("8.8.8/24")][InlineData("8.8.8.8/ 24")][InlineData("not-cidr")][InlineData("2606:4700::%3/64")]
    public void Nonpublic_or_malformed_office_ranges_fail_at_profile_and_write_boundaries(string cidr)
    {
        Assert.False(PublicIpRange.IsPublic(cidr));
        Assert.Throws<ConfigurationException>(() => OfficeLocationValidator.Validate(new[] { new OfficeLocation { Key = "HQ", Name = "Head office", IpRanges = new() { cidr } } }));
        var p = new JsonObject { ["@odata.type"] = "#microsoft.graph.ipNamedLocation", ["displayName"] = "Office", ["isTrusted"] = false,
            ["ipRanges"] = new JsonArray(new JsonObject { ["@odata.type"] = cidr.Contains(':') ? "#microsoft.graph.iPv6CidrRange" : "#microsoft.graph.iPv4CidrRange", ["cidrAddress"] = cidr }) };
        Assert.Throws<SafetyViolationException>(() => WritePayloadGuard.Assert(Standard().Collections["namedLocations"], p));
    }

    [Theory][InlineData("8.8.8.0/24")][InlineData("1.1.1.1/32")][InlineData("2606:4700::/32")]
    public void Public_ranges_are_accepted(string cidr) => Assert.True(PublicIpRange.IsPublic(cidr));

    [Fact]
    public void Offices_require_names_ranges_and_unique_stable_keys_and_names()
    {
        foreach (var text in new[] { "HQ | | 8.8.8.0/24", "HQ | Office |", " | Office | 8.8.8.0/24",
            "HQ | Office | 8.8.8.0/24\nHQ | Other | 1.1.1.1/32", "HQ | Office | 8.8.8.0/24\nTWO | Office | 1.1.1.1/32" })
            Assert.Throws<ConfigurationException>(() => OfficeLocationValidator.ParseLines(text));
    }

    [Fact]
    public void Office_reordering_and_renaming_preserve_identity_and_plan_is_bound_to_profile()
    {
        var s = Standard(); var p = TestData.Profile();
        p.Parameters.OfficeLocations = OfficeLocationValidator.ParseLines("HQ | Head office | 8.8.8.0/24\nTWO | Branch | 2606:4700::/32");
        var snapshot = TestData.Snapshot(s); var mappings = TestData.Mappings(); var session = TestData.Session(); var clock = new FixedClock();
        var plan = new DeploymentPlanner(clock, "test").Build(new PlanRequest { Standard = s, Profile = p, Snapshot = snapshot, Mappings = mappings, Session = session, Deviations = Array.Empty<Deviation>(), SelectedControlIds = new[] { "PRE-008" } });
        Assert.Equal(new[] { "PRE-008-HQ", "PRE-008-TWO" }, plan.Rows.Select(r => r.ControlId));
        Assert.All(plan.Rows, r => { Assert.Equal(PlanAction.Create, r.Action); Assert.False(r.Payload!["isTrusted"]!.GetValue<bool>()); });
        p.Parameters.OfficeLocations.Reverse(); p.Parameters.OfficeLocations.Single(l => l.Key == "HQ").Name = "Renamed";
        Assert.Equal("PRE-008-HQ", ControlInstances.All(s, p).Single(c => c.Name == "Office location: Renamed").Id);
        Assert.Throws<PlanValidationException>(() => DeploymentPlanner.Validate(plan, new PlanValidationContext { Profile = p, Standard = s, Snapshot = snapshot,
            Mappings = mappings, Session = session, Now = clock.UtcNow, AcknowledgedSnapshotId = snapshot.Id }));
        var saved = ProfileValidator.Validate(p, clock.UtcNow);
        Assert.Equal("Renamed", saved.Parameters.OfficeLocations!.Single(l => l.Key == "HQ").Name);
    }

    [Fact]
    public void Schema_additions_do_not_change_older_serialised_evidence()
    {
        var old = ToolkitJson.ToNode(TestData.Standard())!;
        Assert.Null(old["controls"]![0]!["area"]); Assert.False(old["controls"]![0]!.AsObject().ContainsKey("implementation"));
        Assert.False(old["collections"]!["namedLocations"]!.AsObject().ContainsKey("publicIpRangesOnly"));
        Assert.False(ToolkitJson.ToNode(TestData.Profile())!["parameters"]!.AsObject().ContainsKey("officeLocations"));
        var s = Standard(); Assert.Equal(5, s.SchemaVersion); Assert.Equal(86, s.Controls.Count);
        Assert.DoesNotContain(s.Controls, c => c.Id is "SEC-WIN-001" or "SEC-WIN-003" or "CMP-WIN-002");
        Assert.All(new[] { "ENR-003", "ENR-004", "SEC-WIN-002" }, id => Assert.False(s.FindControl(id)!.HasRecipe));
    }

    [Fact]
    public void Hello_complexity_rotation_long_paths_and_all_compliance_actions_are_exact()
    {
        var s = Standard(); var hello = s.FindControl("CFG-WIN-003")!.Payload!["omaSettings"]!.AsArray();
        foreach (var (suffix, value) in new[] { ("/MinimumPINLength", 8), ("/LowercaseLetters", 1), ("/Digits", 0) })
            Assert.Equal(value, hello.Single(x => x!["omaUri"]!.ToString().EndsWith(suffix, StringComparison.Ordinal))!["value"]!.GetValue<int>());
        Assert.Equal("enabledForAzureAdAndHybrid", s.FindControl("CFG-WIN-001")!.Payload!["bitLockerRecoveryPasswordRotation"]!.ToString());
        Assert.DoesNotContain("optional", s.FindControl("CFG-WIN-007")!.Name, StringComparison.OrdinalIgnoreCase);
        var compliance = s.Controls.Where(c => c.HasRecipe && c.Collection is "compliance" or "extendedCompliance").ToList();
        Assert.Equal(4, compliance.Count);
        foreach (var c in compliance)
            foreach (var rule in c.Payload!["scheduledActionsForRule"]!.AsArray())
            {
                var action = Assert.Single(rule!["scheduledActionConfigurations"]!.AsArray())!;
                Assert.Equal(120, action["gracePeriodHours"]!.GetValue<int>()); Assert.Equal("block", action["actionType"]!.ToString()); Assert.Equal(2, action.AsObject().Count);
            }
    }

    [Theory]
    [InlineData("008", "Authentication/EnableWebSignIn", "1")]
    [InlineData("011", "TimeLanguageSettings/ConfigureTimeZone", "GMT Standard Time")]
    [InlineData("014", "Storage/AllowStorageSenseGlobal", "1")]
    [InlineData("015", "Experience/AllowWindowsConsumerFeatures", "0")]
    [InlineData("018", "SettingsSync/EnableWindowsBackup", "<enabled/>")]
    [InlineData("020", "Power/StandbyTimeoutPluggedIn", "<enabled/><data id=\"EnterACStandbyTimeOut\" value=\"0\"/>")]
    [InlineData("021", "WindowsAI/RemoveMicrosoftCopilotApp", "1")]
    public void Each_new_native_payload_pins_path_type_and_value(string suffix, string path, string value)
    {
        var c = Standard().FindControl("CFG-WIN-" + suffix)!; var p = c.Payload!;
        Assert.Equal("#microsoft.graph.windows10CustomConfiguration", p["@odata.type"]!.ToString());
        var setting = Assert.Single(p["omaSettings"]!.AsArray())!;
        Assert.Equal("./Device/Vendor/MSFT/Policy/Config/" + path, setting["omaUri"]!.ToString()); Assert.Equal(value, setting["value"]!.ToString());
        Assert.Equal("#microsoft.graph.omaSetting" + (int.TryParse(value, out _) ? "Integer" : "String"), setting["@odata.type"]!.ToString());
        Assert.Equal("unassigned", c.SafeDeployment.State); Assert.Contains("learn.microsoft.com", c.References.Microsoft);
        p["assignments"] = new JsonArray(); Assert.Throws<SafetyViolationException>(() => WritePayloadGuard.Assert(Standard().Collections[c.Collection!], p));
    }

    [Fact]
    public void All_sixteen_store_payloads_pin_platform_metadata_and_never_ship_assignments()
    {
        var s = Standard(); var names = new[] { "Outlook", "Teams", "Microsoft Authenticator", "OneDrive", "Edge", "Word", "Excel", "Company Portal" };
        foreach (var platform in new[] { "IOS", "AND" })
            for (var i = 1; i <= 8; i++)
            {
                var c = s.FindControl($"APP-{platform}-{i:000}")!; var p = c.Payload!;
                Assert.Equal("#microsoft.graph." + (platform == "IOS" ? "iosStoreApp" : "androidManagedStoreApp"), p["@odata.type"]!.ToString());
                Assert.Contains(names[i - 1], p["displayName"]!.ToString()); Assert.Equal("Microsoft", p["publisher"]!.ToString());
                Assert.Equal("{{" + platform.ToLowerInvariant() + "StoreUrl" + i + "}}", p["appStoreUrl"]!.ToString());
                Assert.Equal(platform == "IOS" ? GraphApi.V1 : GraphApi.Beta, s.Collections[c.Collection!].ApiVersion);
                Assert.Equal("unassigned", c.SafeDeployment.State); Assert.False(p.ContainsKey("assignments"));
                if (platform == "AND") { Assert.Contains("ENR-006", c.Dependencies); Assert.Equal("{{androidPackageId" + i + "}}", p["packageId"]!.ToString()); Assert.False(p["isPrivate"]!.GetValue<bool>()); }
                else { Assert.True(p["applicableDeviceType"]!["iPad"]!.GetValue<bool>()); Assert.True(p["applicableDeviceType"]!["iPhoneAndIPod"]!.GetValue<bool>()); }
                Assert.DoesNotContain(p.Select(x => x.Key), key => key.Contains("vpp", StringComparison.OrdinalIgnoreCase));
                p["assignments"] = new JsonArray(); Assert.Throws<SafetyViolationException>(() => WritePayloadGuard.Assert(s.Collections[c.Collection!], p));
            }
    }

    [Fact]
    public void Admin_strength_is_disabled_with_operator_and_emergency_exclusions_and_unknown_never_missing()
    {
        var s = Standard(); var p = TestData.Profile(); var snapshot = TestData.Snapshot(s); var session = TestData.Session();
        var plan = new DeploymentPlanner(new FixedClock(), "test").Build(new PlanRequest { Standard = s, Profile = p, Snapshot = snapshot,
            Mappings = TestData.Mappings(), Session = session, Deviations = Array.Empty<Deviation>(), SelectedControlIds = new[] { "CA-011" } });
        var row = Assert.Single(plan.Rows); Assert.Equal(PlanAction.Create, row.Action);
        Assert.Equal("disabled", row.Payload!["state"]!.ToString());
        Assert.Equal("00000000-0000-0000-0000-000000000004", row.Payload["grantControls"]!["authenticationStrength"]!["id"]!.ToString());
        Assert.Equal(14, row.Payload["conditions"]!["users"]!["includeRoles"]!.AsArray().Count);
        Assert.Contains(TestData.Operator, ConditionalAccessSafety.ExcludedUsers(row.Payload)); Assert.Contains(TestData.Emergency, ConditionalAccessSafety.ExcludedUsers(row.Payload));
        row.Payload["conditions"]!["users"]!["excludeUsers"] = new JsonArray(TestData.Emergency); plan.PlanDigest = DeploymentPlanner.ComputeDigest(plan);
        Assert.Throws<PlanValidationException>(() => DeploymentPlanner.Validate(plan, new PlanValidationContext { Standard = s, Profile = p, Snapshot = snapshot,
            Mappings = TestData.Mappings(), Session = session, Now = new FixedClock().UtcNow, AcknowledgedSnapshotId = snapshot.Id }));
        snapshot.Collections["conditionalAccess"].Status = CaptureStatus.Error;
        var assessed = new AssessmentEngine(new FixedClock(), "test").Assess(snapshot, s, p, TestData.Mappings(), Array.Empty<Deviation>(), "synthetic");
        Assert.Equal(FindingStatus.UnableToAssess, assessed.Findings.Single(f => f.ControlId == "CA-011").Status);
        Assert.Equal(FindingStatus.UnableToAssess, assessed.Findings.Single(f => f.ControlId == "ID-004").Status);
    }

    [Fact]
    public void Provisioning_group_is_empty_and_named_locations_untrusted_with_transport_enforcement()
    {
        var s = Standard(); var group = s.FindControl("PRE-011")!.Payload!;
        WritePayloadGuard.Assert(s.Collections["groups"], group);
        Assert.True(group["securityEnabled"]!.GetValue<bool>()); Assert.False(group["mailEnabled"]!.GetValue<bool>()); Assert.Empty(group["groupTypes"]!.AsArray());
        group["owners@odata.bind"] = new JsonArray("https://graph.microsoft.com/v1.0/directoryObjects/" + TestData.Operator);
        Assert.Throws<SafetyViolationException>(() => WritePayloadGuard.Assert(s.Collections["groups"], group));
        var profile = TestData.Profile(); profile.Parameters.OfficeLocations = OfficeLocationValidator.ParseLines("HQ | Office | 8.8.8.0/24");
        var location = ControlInstances.Find(s, profile, "PRE-008-HQ")!.Payload!; location["isTrusted"] = true;
        Assert.Throws<SafetyViolationException>(() => WritePayloadGuard.Assert(s.Collections["namedLocations"], location));
        var routes = GraphRouteAllowList.FromStandard(s);
        Assert.True(routes.MatchWrite(GraphApi.V1, "/identity/conditionalAccess/namedLocations", out _)!.PublicIpRangesOnly);
        Assert.Null(routes.MatchWrite(GraphApi.V1, "/groups/" + TestData.Office, out _));
    }

    [Fact]
    public async Task Schema_five_executor_refuses_a_missing_typed_confirmation_before_any_write()
    {
        using var root = new TempRoot(); var s = TestData.Standard(); s.SchemaVersion = 5;
        var snapshot = TestData.Snapshot(s); var p = TestData.Profile(); var session = TestData.Session(); var mappings = TestData.Mappings(); var clock = new FixedClock();
        var evidence = new EvidenceStore(root.Paths, NullLog.Instance); evidence.SaveSnapshot(snapshot);
        var plan = new DeploymentPlanner(clock, "test").Build(new PlanRequest { Standard = s, Snapshot = snapshot, Profile = p, Session = session, Mappings = mappings, Deviations = Array.Empty<Deviation>(), SelectedControlIds = new[] { "CA-003" } });
        var graph = new FakeGraphClient(s); var executor = new DeploymentExecutor(evidence, new TenantCollector(NullLog.Instance, clock, "test"), NullLog.Instance, clock, "test");
        await Assert.ThrowsAsync<SafetyViolationException>(() => executor.StartAsync(new ExecutionRequest { Standard = s, Snapshot = snapshot, Profile = p, Session = session,
            Mappings = mappings, Plan = plan, Graph = graph, AcknowledgedSnapshotId = snapshot.Id }, new(), null)); Assert.Empty(graph.Writes);
    }
}
