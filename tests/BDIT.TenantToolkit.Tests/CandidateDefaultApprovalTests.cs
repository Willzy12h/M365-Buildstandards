using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Execution;
using BDIT.TenantToolkit.Engine.Planning;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class CandidateDefaultApprovalTests
{
    private const string ComplianceId = "CMP-WIN-001";

    private static StandardCatalogue AndroidStandard()
    {
        var standard = TestData.Standard();
        standard.Parameters.Add(new ParameterDefinition
        {
            Key = "androidMinimumVersion", Label = "Minimum Android version", Type = "string",
            Default = JsonValue.Create("14"), ReviewedOn = "2026-09-10"
        });
        standard.FindControl(ComplianceId)!.Payload!["osMinimumVersion"] = "{{androidMinimumVersion}}";
        return standard;
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("14", true)]
    [InlineData("15", false)]
    public async Task Assignment_requires_confirmation_of_the_value_actually_created(string? confirmed, bool allowed)
    {
        using var h = new RecoveryTests.Harness(AndroidStandard());
        var run = await h.Deploy(ComplianceId);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("14", h.Object(run)["osMinimumVersion"]!.GetValue<string>());
        h.Profile.Parameters.PolicyInputs = confirmed is null ? null : new() { ["androidMinimumVersion"] = JsonValue.Create(confirmed) };
        var snapshot = await h.Capture();
        var service = new ReviewedChangeService(h.Evidence, h.Clock);
        Task<ReviewedChangePlan> Preview() => service.PreviewAsync(h.Graph, h.Session, h.Profile, h.Standard, snapshot,
            ReviewedChangeKind.AssignGroups, ComplianceId, run.Results.Single().ObjectId!, Array.Empty<string>(), Array.Empty<string>(), default,
            population: AssignmentPopulation.AllDevices);

        if (allowed)
        {
            var plan = await Preview();
            Assert.Equal(AssignmentPopulation.AllDevices, plan.Population);
        }
        else
        {
            var error = await Assert.ThrowsAsync<SafetyViolationException>(Preview);
            Assert.Contains(confirmed is null ? "Confirm and save" : "Re-plan and update", error.Message, StringComparison.Ordinal);
        }
        Assert.Single(h.Graph.Writes);
    }

    [Fact]
    public async Task Replanning_a_supported_inactive_candidate_with_confirmed_values_unblocks_assignment()
    {
        const string controlId = "CFG-ANDROID-001";
        var standard = TestData.Standard();
        standard.Parameters.Add(new ParameterDefinition { Key = "minimumPasscodeLength", Label = "Minimum passcode length", Type = "integer", Default = JsonValue.Create(6) });
        standard.Collections["configuration"] = new CollectionDefinition
        {
            Path = "/deviceManagement/deviceConfigurations", Scope = "DeviceManagementConfiguration.Read.All",
            Write = "DeviceManagementConfiguration.ReadWrite.All", Assignments = true
        };
        standard.Controls.Add(new ControlDefinition
        {
            Id = controlId, Collection = "configuration", Assessment = new AssessmentRule { Mode = AssessmentMode.Settings },
            Payload = new JsonObject { ["@odata.type"] = "#microsoft.graph.androidGeneralDeviceConfiguration", ["displayName"] = "Synthetic Android passcode",
                ["passwordRequired"] = true, ["passwordMinimumLength"] = "{{minimumPasscodeLength}}" }
        });
        using var h = new RecoveryTests.Harness(standard);
        Assert.Equal(RunStatus.Completed, (await h.Deploy(controlId)).Status);
        h.Profile.Parameters.PolicyInputs = new() { ["minimumPasscodeLength"] = JsonValue.Create(8) };
        var updated = await h.Deploy(controlId);
        Assert.Equal(RunStatus.Completed, updated.Status);
        Assert.Equal(nameof(PlanAction.Update), updated.Results.Single().PlannedAction);
        Assert.Equal(8, h.Object(updated)["passwordMinimumLength"]!.GetValue<int>());

        var preview = await new ReviewedChangeService(h.Evidence, h.Clock).PreviewAsync(h.Graph, h.Session, h.Profile, h.Standard,
            await h.Capture(), ReviewedChangeKind.AssignGroups, controlId, updated.Results.Single().ObjectId!,
            Array.Empty<string>(), Array.Empty<string>(), default, population: AssignmentPopulation.AllDevices);
        Assert.Equal(AssignmentPopulation.AllDevices, preview.Population);
    }

    [Fact]
    public async Task Related_settings_candidates_still_require_the_separate_reviewed_update_procedure()
    {
        using var h = new RecoveryTests.Harness(AndroidStandard());
        Assert.Equal(RunStatus.Completed, (await h.Deploy(ComplianceId)).Status);
        h.Profile.Parameters.PolicyInputs = new() { ["androidMinimumVersion"] = JsonValue.Create("15") };
        var plan = new DeploymentPlanner(h.Clock, "test").Build(new PlanRequest
        {
            Profile = h.Profile, Standard = h.Standard, Snapshot = await h.Capture(), Session = h.Session,
            Mappings = h.Evidence.LoadMappings(h.Profile.TenantId), Deviations = Array.Empty<Deviation>(), SelectedControlIds = new[] { ComplianceId }
        });
        var row = Assert.Single(plan.Rows);
        Assert.Equal(PlanAction.Manual, row.Action);
        Assert.Contains("related settings", row.Reason, StringComparison.Ordinal);
        Assert.Single(h.Graph.Writes);
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(24, false)]
    public async Task Conditional_access_confirmation_preserves_injected_operator_and_emergency_exclusions(int confirmed, bool allowed)
    {
        var standard = TestData.Standard();
        standard.Parameters.Add(new ParameterDefinition
        {
            Key = "signInHours", Label = "Sign-in frequency", Type = "integer",
            Default = JsonValue.Create(12), ReviewedOn = "2026-09-10"
        });
        standard.FindControl("CA-001")!.Payload!["sessionControls"] = new JsonObject
        {
            ["signInFrequency"] = new JsonObject { ["value"] = "{{signInHours}}", ["type"] = "hours", ["isEnabled"] = true }
        };
        using var h = new RecoveryTests.Harness(standard);
        const string secondEmergency = "99999999-9999-4999-8999-999999999999";
        h.Profile.Parameters.EmergencyAccountIds.Add(secondEmergency);
        h.Graph.Add("/users", new JsonObject { ["id"] = secondEmergency, ["displayName"] = "Second emergency account" });
        var run = await h.Deploy();
        Assert.Equal(RunStatus.Completed, run.Status);
        h.Profile.Parameters.PolicyInputs = new() { ["signInHours"] = JsonValue.Create(confirmed) };
        var snapshot = await h.Capture();
        var service = new ReviewedChangeService(h.Evidence, h.Clock);
        Task<ReviewedChangePlan> Preview() => service.PreviewAsync(h.Graph, h.Session, h.Profile, h.Standard, snapshot,
            ReviewedChangeKind.EnableConditionalAccess, "CA-001", run.Results.Single().ObjectId!,
            Array.Empty<string>(), Array.Empty<string>(), default);
        if (allowed)
        {
            var plan = await Preview();
            Assert.Contains(TestData.Operator, ConditionalAccessSafety.ExcludedUsers(plan.Before));
            Assert.Contains(TestData.Emergency, ConditionalAccessSafety.ExcludedUsers(plan.Before));
            Assert.Contains(secondEmergency, ConditionalAccessSafety.ExcludedUsers(plan.Before));
            Assert.Single(plan.Payload);
            Assert.Equal("enabled", plan.Payload["state"]!.GetValue<string>());
        }
        else
        {
            var error = await Assert.ThrowsAsync<SafetyViolationException>(Preview);
            Assert.Contains("Re-plan and update", error.Message, StringComparison.Ordinal);
        }
        Assert.Single(h.Graph.Writes);
    }

    [Fact]
    public async Task Removing_assignments_is_not_blocked_by_unconfirmed_defaults()
    {
        using var h = new RecoveryTests.Harness(AndroidStandard());
        var run = await h.Deploy(ComplianceId);
        Assert.Equal(RunStatus.Completed, run.Status);
        var plan = await new ReviewedChangeService(h.Evidence, h.Clock).PreviewAsync(h.Graph, h.Session, h.Profile, h.Standard,
            await h.Capture(), ReviewedChangeKind.RemoveAssignments, ComplianceId, run.Results.Single().ObjectId!,
            Array.Empty<string>(), Array.Empty<string>(), default);
        Assert.Empty((JsonArray)plan.Payload["assignments"]!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Esp_application_confirmation_must_match_the_candidate_list(bool containsApplication)
    {
        const string appId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
        var standard = new StandardCatalogue();
        standard.Parameters.Add(new ParameterDefinition
        {
            Key = "espBlockingAppIds", Label = "ESP blocking applications", Type = "guidList", Required = false,
            Default = new JsonArray(), ReviewedOn = "2026-09-10"
        });
        var payload = new JsonObject { ["selectedMobileAppIds"] = "{{espBlockingAppIds}}" };
        var recorded = new JsonObject { ["selectedMobileAppIds"] = new JsonArray() };
        var approved = new Dictionary<string, JsonNode?> { ["espBlockingAppIds"] = containsApplication ? new JsonArray(appId) : new JsonArray() };
        void Check() => PolicyInputDefaults.AssertCandidateUsesConfirmedInputs(payload, standard, approved, recorded, new FixedClock().UtcNow);
        if (containsApplication) Assert.Throws<SafetyViolationException>(Check);
        else Check();
    }

    [Fact]
    public void The_optional_esp_default_resolves_but_other_empty_identity_lists_still_fail()
    {
        var standard = new StandardCatalogue();
        var esp = new ParameterDefinition { Key = "espBlockingAppIds", Label = "Blocking applications", Type = "guidList", Default = new JsonArray(), Required = false };
        standard.Parameters.Add(esp);
        standard.Parameters.Add(new ParameterDefinition { Key = "emergencyAccountIds", Label = "Emergency accounts", Type = "guidList", Required = true });
        var payload = new JsonObject { ["selectedMobileAppIds"] = "{{espBlockingAppIds}}" };
        var defaults = PolicyInputDefaults.Apply(payload, standard, new Dictionary<string, JsonNode?>(), new FixedClock().UtcNow);
        var resolved = PolicyInputDefaults.Resolve(payload, standard, defaults.Values)!;
        Assert.Empty(resolved["selectedMobileAppIds"]!.AsArray());
        Assert.Single(defaults.Warnings);
        esp.Required = true;
        Assert.Throws<MissingParameterException>(() => PolicyInputDefaults.Resolve(payload, standard, defaults.Values));
        esp.Required = false;
        var identity = new JsonObject { ["excludeUsers"] = "{{emergencyAccountIds}}" };
        var values = new Dictionary<string, JsonNode?> { ["emergencyAccountIds"] = new JsonArray() };
        Assert.Throws<MissingParameterException>(() => PolicyInputDefaults.Resolve(identity, standard, values));
        standard.Parameters[1].Required = false;
        Assert.Throws<MissingParameterException>(() => PolicyInputDefaults.Resolve(identity, standard, values));
    }

    [Fact]
    public void Nested_array_and_embedded_scalar_inputs_are_checked_without_comparing_unrelated_fields()
    {
        var standard = new StandardCatalogue();
        standard.Parameters.Add(new ParameterDefinition { Key = "version", Label = "OS version", Type = "string", Default = JsonValue.Create("14") });
        var payload = JsonNode.Parse("""{"settings":[{"name":"Minimum version {{version}}","value":"{{version}}"}],"excludeUsers":"{{emergencyAccountIds}}"}""")!.AsObject();
        var recorded = JsonNode.Parse("""{"settings":[{"name":"Minimum version 14","value":"14"}],"excludeUsers":["emergency","operator"]}""")!.AsObject();
        var values = new Dictionary<string, JsonNode?> { ["version"] = JsonValue.Create("14") };
        PolicyInputDefaults.AssertCandidateUsesConfirmedInputs(payload, standard, values, recorded, new FixedClock().UtcNow);
        recorded["settings"]![0]!["value"] = "13";
        Assert.Throws<SafetyViolationException>(() => PolicyInputDefaults.AssertCandidateUsesConfirmedInputs(payload, standard, values, recorded, new FixedClock().UtcNow));
    }

    [Theory]
    [InlineData("", "missing or invalid")]
    [InlineData("not-a-date", "missing or invalid")]
    [InlineData("2027-01-01", "in the future")]
    public void An_unusable_review_date_is_not_described_as_an_old_review(string reviewDate, string expectedWarning)
    {
        var standard = AndroidStandard();
        standard.Parameters.Single(p => p.Key == "androidMinimumVersion").ReviewedOn = reviewDate;
        var applied = PolicyInputDefaults.Apply(standard.FindControl(ComplianceId)!.Payload!, standard,
            new Dictionary<string, JsonNode?>(), new FixedClock().UtcNow);
        var warning = Assert.Single(applied.Warnings);
        Assert.Contains(expectedWarning, warning, StringComparison.Ordinal);
        Assert.DoesNotContain("more than 90 days ago", warning, StringComparison.Ordinal);
    }
}
