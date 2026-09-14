using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Planning;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class DevicePolicyImportTests
{
    internal static StandardCatalogue Shipped() => StandardsLoader.Parse(File.ReadAllText(Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards", "2026.09.5.json"))), "2026.09.5.json");

    [Fact]
    public void Release_has_eighteen_recipes_and_correct_new_CSP_defaults()
    {
        var s = Shipped(); Assert.Equal(45, s.Controls.Count); Assert.Equal(18, s.Controls.Count(c => c.HasRecipe));
        Assert.Equal(15, s.Collections.Count);
        var laps = s.FindControl("CFG-WIN-002")!.Payload!["omaSettings"]!.AsArray().OfType<JsonObject>().ToDictionary(x => x["displayName"]!.ToString(), x => x["value"]!.GetValue<int>());
        Assert.Equal(1, laps["BackupDirectory"]); Assert.Equal(20, laps["PasswordLength"]); Assert.Equal(7, laps["PasswordAgeDays"]);
        Assert.Equal(3, laps["PostAuthenticationActions"]);
        var firewall = s.FindControl("SEC-WIN-003")!.Payload!["omaSettings"]!.AsArray().OfType<JsonObject>();
        foreach (var value in firewall.Where(x => x["omaUri"]!.ToString().EndsWith("DefaultInboundAction", StringComparison.Ordinal))) Assert.Equal(1, value["value"]!.GetValue<int>());
        foreach (var value in firewall.Where(x => x["omaUri"]!.ToString().EndsWith("EnableFirewall", StringComparison.Ordinal))) Assert.True(value["value"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("CFG-WIN-002")]
    [InlineData("SEC-WIN-001")]
    [InlineData("SEC-WIN-002")]
    [InlineData("SEC-WIN-003")]
    public void Imported_candidate_strips_source_ID_and_assignments_without_modifying_baseline(string id)
    {
        var s = Shipped(); var before = ToolkitJson.Serialize(s); var export = (JsonObject)s.FindControl(id)!.Payload!.DeepClone();
        export["id"] = TestData.TenantB; export["assignments"] = new JsonArray(new JsonObject { ["target"] = new JsonObject { ["groupId"] = TestData.Office } });
        export["roleScopeTagIds"] = new JsonArray("9");
        var result = DevicePolicyImporter.Import(s, id, export.ToJsonString(), "Reviewed candidate");
        Assert.Equal(before, ToolkitJson.Serialize(s)); Assert.NotEqual(s.Release, result.Standard.Release);
        var payload = result.Standard.FindControl(id)!.Payload!;
        Assert.Null(payload["id"]); Assert.Null(payload["assignments"]); Assert.Null(payload["roleScopeTagIds"]);
        Assert.Equal("Reviewed candidate", payload["displayName"]!.ToString()); Assert.Contains("assignments", result.RemovedProperties);
        Assert.Equal(CanonicalJson.Sha256Hex(export.ToJsonString()), result.SourceDigest);
        Assert.False(payload.ToJsonString().Contains(TestData.TenantB, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("wrong-type")]
    [InlineData("new-uri")]
    [InlineData("template")]
    public void Unsupported_or_ambiguous_OMA_settings_are_rejected(string change)
    {
        var s = Shipped(); var p = (JsonObject)s.FindControl("CFG-WIN-002")!.Payload!.DeepClone(); var values = p["omaSettings"]!.AsArray();
        switch (change)
        {
            case "unknown": p["unreviewedSetting"] = true; break;
            case "missing": values.RemoveAt(0); break;
            case "duplicate": values[1] = values[0]!.DeepClone(); break;
            case "wrong-type": values[0]!["value"] = "1"; break;
            case "new-uri": values[0]!["omaUri"] = "./Device/Vendor/MSFT/Unreviewed/Execute"; break;
            case "template": values[0]!["displayName"] = "{{tenantId}}"; break;
        }
        Assert.Throws<ConfigurationException>(() => DevicePolicyImporter.Import(s, "CFG-WIN-002", p.ToJsonString(), "Candidate"));
    }

    [Theory]
    [InlineData("{\"id\":1,\"ID\":2}")]
    [InlineData("[]")]
    [InlineData("{bad json")]
    public void Duplicate_keys_lists_and_invalid_JSON_are_rejected(string json) =>
        Assert.Throws<ConfigurationException>(() => DevicePolicyImporter.Import(Shipped(), "CFG-WIN-002", json, "Candidate"));

    [Fact]
    public void Import_is_bounded_and_cannot_accept_arbitrary_controls_or_EDR_blobs()
    {
        var s = Shipped();
        Assert.Throws<ConfigurationException>(() => DevicePolicyImporter.Import(s, "CA-001", "{}", "Candidate"));
        Assert.Throws<ConfigurationException>(() => DevicePolicyImporter.Import(s, "CFG-WIN-002", new string(' ', 256 * 1024 + 1), "Candidate"));
        var p = s.FindControl("SEC-WIN-002")!.Payload!; p["advancedThreatProtectionOnboardingBlob"] = "synthetic-source-tenant-data";
        // The transport guard is independent of import; a caller cannot bypass it with an edited catalogue.
        Assert.Throws<SafetyViolationException>(() => WritePayloadGuard.Assert(s.Collections["endpointProtection"], p));
        var clean = Shipped();
        Assert.Throws<ConfigurationException>(() => DevicePolicyImporter.Import(clean, "SEC-WIN-002", p.ToJsonString(), "Candidate"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LAPS_candidate_requires_positive_Entra_prerequisite_evidence(bool enabled)
    {
        var s = Shipped(); var snapshot = TestData.Snapshot(s);
        snapshot.Collections["deviceRegistration"].Items.Add(EntraLapsTests.Policy(enabled)); snapshot.Collections["deviceRegistration"].Count = 1;
        var problem = DeviceCandidateReadiness.Problem(s.FindControl("CFG-WIN-002")!.Payload!, s, snapshot);
        if (enabled) Assert.Null(problem); else Assert.Contains("disabled", problem);
        snapshot.Collections["deviceRegistration"].Items.Clear(); Assert.NotNull(DeviceCandidateReadiness.Problem(s.FindControl("CFG-WIN-002")!.Payload!, s, snapshot));
    }

    [Theory]
    [InlineData("INTUNE_A", false)]
    [InlineData("MDE_LITE", false)]
    [InlineData("MDE_SMB", true)]
    [InlineData("WINDEFATP", true)]
    public void EDR_requires_an_EDR_entitlement_not_just_Intune_or_Endpoint_P1(string plan, bool allowed)
    {
        var s = Shipped(); var snapshot = TestData.Snapshot(s);
        snapshot.Collections["licences"].Items[0]["servicePlans"] = new JsonArray(new JsonObject { ["servicePlanName"] = plan, ["provisioningStatus"] = "Success" });
        var reason = DeviceCandidateReadiness.Problem(s.FindControl("SEC-WIN-002")!.Payload!, s, snapshot);
        if (allowed) Assert.Null(reason); else Assert.NotNull(reason);
    }

    [Theory]
    [InlineData("CFG-WIN-002")]
    [InlineData("SEC-WIN-001")]
    [InlineData("SEC-WIN-002")]
    [InlineData("SEC-WIN-003")]
    public async Task New_candidates_follow_existing_durable_deployment_and_exact_ID_recovery(string id)
    {
        var shipped = Shipped(); var control = shipped.FindControl(id)!; var s = TestData.Standard();
        s.Controls.Add(control); s.Collections[control.Collection!] = shipped.Collections[control.Collection!];
        s.Collections["deviceRegistration"] = shipped.Collections["deviceRegistration"];
        using var h = new RecoveryTests.Harness(s);
        h.Graph.SetSingleton(EntraLapsSafety.Path, EntraLapsTests.Policy(true));
        h.Graph.Collection("/subscribedSkus")[0]["servicePlans"]!.AsArray().Add(new JsonObject { ["servicePlanName"] = "MDE_SMB", ["provisioningStatus"] = "Success" });
        var run = await h.Deploy(id); Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(ConfigurationVerification.Pass, run.Results.Single().Configuration); Assert.Null(h.Graph.Writes.Single().Payload["assignments"]);
        h.Object(run)["_assignments"] = new JsonArray(new JsonObject { ["id"] = TestData.Office });
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Preview(run)); Assert.Empty(h.Graph.RecoveryWrites);
        h.Object(run)["_assignments"] = new JsonArray();
        var recovery = await h.Execute(await h.Preview(run)); Assert.True(recovery.ObjectAbsent);
        Assert.Equal(ConfigurationVerification.Pass, recovery.Verification);
    }
}
