using System.Net;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Collection;
using BDIT.TenantToolkit.Engine.Execution;
using BDIT.TenantToolkit.Engine.Recovery;
using BDIT.TenantToolkit.Engine.Standards;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using Xunit;
using Harness = BDIT.TenantToolkit.Tests.RecoveryTests.Harness;

namespace BDIT.TenantToolkit.Tests;

public sealed class SafetyReviewRegressionTests
{
    private static Harness CaHarness()
    {
        var h = new Harness();
        const string second = "99999999-9999-4999-8999-999999999999";
        h.Profile.Parameters.EmergencyAccountIds.Add(second);
        h.Graph.Add("/users", new JsonObject { ["id"] = second, ["displayName"] = "Synthetic emergency account" });
        return h;
    }

    private static async Task<ReviewedChangePlan> Preview(Harness h, DeploymentRun run, ReviewedChangeKind kind = ReviewedChangeKind.EnableConditionalAccess) =>
        await new ReviewedChangeService(h.Evidence, h.Clock).PreviewAsync(h.Graph, h.Session, h.Profile, h.Standard,
            await h.Capture(), kind, run.Results.Single().ControlId, run.Results.Single().ObjectId!, [], [], default);

    [Theory]
    [InlineData("applicationExclusion")]
    [InlineData("locationExclusion")]
    [InlineData("deviceFilter")]
    [InlineData("sessionControl")]
    [InlineData("unknownControl")]
    public async Task Ca_material_additions_block_service_preview_before_any_mutation(string change)
    {
        using var h = CaHarness(); var run = await h.Deploy();
        Assert.Equal(ConfigurationVerification.Pass, run.Results.Single().Configuration);
        var original = File.ReadAllBytes(h.Evidence.RunFile(run));
        var current = h.Object(run);
        switch (change)
        {
            case "applicationExclusion": current["conditions"]!["applications"]!["excludeApplications"] = new JsonArray("Office365"); break;
            case "locationExclusion": current["conditions"]!["locations"] = new JsonObject { ["excludeLocations"] = new JsonArray(TestData.Office) }; break;
            case "deviceFilter": current["conditions"]!["devices"] = new JsonObject { ["deviceFilter"] = new JsonObject { ["mode"] = "exclude", ["rule"] = "device.isCompliant -eq True" } }; break;
            case "sessionControl": current["sessionControls"] = new JsonObject { ["persistentBrowser"] = new JsonObject { ["isEnabled"] = true, ["mode"] = "always" } }; break;
            default: current["futureMaterialControl"] = true; break;
        }
        await Assert.ThrowsAsync<SafetyViolationException>(() => Preview(h, run));
        await Assert.ThrowsAsync<SafetyViolationException>(() => Preview(h, run, ReviewedChangeKind.ReportOnlyConditionalAccess));
        Assert.Single(h.Graph.Writes); Assert.Empty(h.Graph.RecoveryWrites);
        Assert.Equal(original, File.ReadAllBytes(h.Evidence.RunFile(run)));
        // Emergency containment deliberately does not require unchanged targeting.
        Assert.Equal("disabled", (await Preview(h, run, ReviewedChangeKind.DisableConditionalAccess)).Payload["state"]!.ToString());
    }

    [Fact]
    public async Task Ca_harmless_service_metadata_and_neutral_empty_exclusions_do_not_block_preview()
    {
        using var h = CaHarness(); var run = await h.Deploy(); var current = h.Object(run);
        current["modifiedDateTime"] = "2026-09-21T00:00:00Z";
        current["@odata.etag"] = "synthetic-etag";
        current["conditions"]!["applications"]!["excludeApplications"] = new JsonArray();
        current["grantControls"]!["authenticationStrength"] = null;
        current["sessionControls"] = new JsonObject { ["applicationEnforcedRestrictions"] = null,
            ["cloudAppSecurity"] = null, ["persistentBrowser"] = null, ["signInFrequency"] = null };
        Assert.Equal("enabled", (await Preview(h, run)).Payload["state"]!.ToString());
        Assert.Single(h.Graph.Writes);
    }

    [Fact]
    public async Task Ca_missing_verified_baseline_cannot_use_mapping_payload_as_evidence()
    {
        using var h = CaHarness(); var run = await h.Deploy();
        run.Results.Single().AfterObject = null; run.Results.Single().ReadbackDigest = null;
        h.Evidence.SaveRun(run);
        await Assert.ThrowsAsync<SafetyViolationException>(() => Preview(h, run));
        Assert.Single(h.Graph.Writes);
    }

    private sealed class Tokens : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult("synthetic-token");
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => GetAccessTokenAsync(ct);
    }
    private sealed class Requests : HttpMessageHandler
    {
        public List<(string Method, string Uri, string Body)> Sent { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sent.Add((request.Method.Method, request.RequestUri!.ToString(), request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
    private static GraphClient Client(Requests handler) => new(new HttpClient(handler), new Tokens(), TestData.TenantA,
        SessionMode.Deployment, GraphRouteAllowList.Only(new[] {
            new GraphRoute(GraphApi.Beta, "/deviceManagement/deviceEnrollmentConfigurations", "DeviceManagementServiceConfig.Read.All", "DeviceManagementServiceConfig.ReadWrite.All", "enrolment"),
            new GraphRoute(GraphApi.Beta, "/deviceManagement/windowsAutopilotDeploymentProfiles", "DeviceManagementServiceConfig.Read.All", "DeviceManagementServiceConfig.ReadWrite.All", "autopilot") }),
        new GraphClientOptions { Sleep = false }, NullLog.Instance);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enrolment_assignment_HTTP_uses_documented_envelope(bool remove)
    {
        const string root = "/deviceManagement/deviceEnrollmentConfigurations";
        var plan = new ReviewedChangePlan { TenantId = TestData.TenantA, Kind = remove ? ReviewedChangeKind.RemoveAssignments : ReviewedChangeKind.AssignGroups,
            Api = GraphApi.Beta, Method = "POST", ObjectId = TestData.Office, Path = root + "/" + TestData.Office + "/assign",
            RequiredScope = "DeviceManagementServiceConfig.ReadWrite.All", IncludeGroups = remove ? [] : [TestData.Mam],
            Payload = ReviewedChangeSafety.AssignmentPayload(root, remove ? [] : [TestData.Mam], []) };
        var handler = new Requests(); await Client(handler).ApplyReviewedChangeAsync(plan, default);
        var sent = Assert.Single(handler.Sent);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("https://graph.microsoft.com/beta" + root + "/" + TestData.Office + "/assign", sent.Uri);
        // Deliberately not constructed with the production builder.
        var expected = remove ? "{\"enrollmentConfigurationAssignments\":[]}"
            : $$$"""{"enrollmentConfigurationAssignments":[{"@odata.type":"#microsoft.graph.enrollmentConfigurationAssignment","target":{"@odata.type":"#microsoft.graph.groupAssignmentTarget","groupId":"{{{TestData.Mam}}}"}}]}""";
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(sent.Body)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Autopilot_generic_assignment_and_empty_removal_never_reach_transport(bool remove)
    {
        const string root = "/deviceManagement/windowsAutopilotDeploymentProfiles";
        var plan = new ReviewedChangePlan { TenantId = TestData.TenantA, Kind = remove ? ReviewedChangeKind.RemoveAssignments : ReviewedChangeKind.AssignGroups,
            Api = GraphApi.Beta, Method = "POST", ObjectId = TestData.Office, Path = root + "/" + TestData.Office + "/assign",
            RequiredScope = "DeviceManagementServiceConfig.ReadWrite.All", IncludeGroups = remove ? [] : [TestData.Mam],
            Payload = new JsonObject { ["assignments"] = remove ? new JsonArray() : new JsonArray(new JsonObject {
                ["@odata.type"] = "#microsoft.graph.windowsAutopilotDeploymentProfileAssignment",
                ["target"] = new JsonObject { ["@odata.type"] = "#microsoft.graph.groupAssignmentTarget", ["groupId"] = TestData.Mam } }) } };
        var handler = new Requests();
        var failure = await Assert.ThrowsAsync<WriteNotSentException>(() => Client(handler).ApplyReviewedChangeAsync(plan, default));
        Assert.IsType<SafetyViolationException>(failure.InnerException);
        Assert.Contains("Autopilot", failure.Message);
        Assert.Empty(handler.Sent);
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.AssignmentPayload(root, remove ? [] : [TestData.Mam], []));
    }

    [Fact]
    public async Task Beta_edr_restore_later_verifies_using_reads_only_and_preserves_original_evidence()
    {
        var standard = TestData.Standard();
        standard.Collections["edr"] = new CollectionDefinition { Api = "beta", Path = "/deviceManagement/deviceConfigurations",
            Write = "DeviceManagementConfiguration.ReadWrite.All", Scope = "DeviceManagementConfiguration.Read.All", Assignments = true };
        standard.Controls.Add(new ControlDefinition { Id = "EDR-TEST", Name = "Synthetic EDR", Collection = "edr",
            Assessment = new AssessmentRule { Mode = AssessmentMode.Settings }, SafeDeployment = new SafeDeployment { State = "unassigned" },
            Payload = new JsonObject { ["@odata.type"] = "#microsoft.graph.windowsDefenderAdvancedThreatProtectionConfiguration",
                ["displayName"] = "Synthetic EDR", ["advancedThreatProtectionAutoPopulateOnboardingBlob"] = true } });
        using var h = new Harness(standard);
        h.Graph.Collection("/subscribedSkus")[0]["servicePlans"]!.AsArray().Add(new JsonObject { ["servicePlanName"] = "MDE_SMB", ["provisioningStatus"] = "Success" });
        var created = await h.Deploy("EDR-TEST");
        Assert.Equal(RunStatus.Completed, created.Status);
        var obj = h.Object(created); var mappings = h.Evidence.LoadMappings(h.Session.TenantId);
        var oldMapping = mappings.Find("EDR-TEST")!;
        var update = ToolkitJson.Deserialize<DeploymentRun>(ToolkitJson.Serialize(created)); update.Id = Guid.NewGuid().ToString();
        var item = update.Results.Single(); item.PlannedAction = "Update";
        item.BeforeObject = await RecoveryObjectReader.ReadAsync(h.Graph, standard.Collections["edr"], item.ObjectId!, default); item.BeforeMapping = oldMapping;
        item.WrittenPayload = (JsonObject)standard.FindControl("EDR-TEST")!.Payload!.DeepClone(); item.WrittenPayload["displayName"] = "Updated synthetic EDR";
        item.PayloadDigest = CanonicalJson.Sha256(item.WrittenPayload);
        obj["displayName"] = "Updated synthetic EDR";
        item.AfterObject = await RecoveryObjectReader.ReadAsync(h.Graph, standard.Collections["edr"], item.ObjectId!, default);
        item.ReadbackDigest = CanonicalJson.Sha256(item.AfterObject);
        h.Evidence.SaveRun(update);
        mappings = h.Evidence.LoadMappings(h.Session.TenantId); var mapping = mappings.Find("EDR-TEST")!;
        mapping.RunId = update.Id; mapping.LastApplied = (JsonObject)item.WrittenPayload.DeepClone(); mapping.LastAppliedDigest = item.PayloadDigest;
        h.Evidence.SaveMappings(mappings);
        var plan = await h.Preview(update, RecoveryAction.RestoreUpdate);
        h.Graph.BeforeRecovery = () => { h.Graph.MutateReadback = _ => throw new IOException("Synthetic initial read failure"); return Task.CompletedTask; };
        var run = await h.Execute(plan); Assert.Equal(WriteAcceptance.Accepted, run.WriteAcceptance); Assert.Equal(ConfigurationVerification.Unknown, run.Verification);
        var original = ToolkitJson.Serialize(h.Evidence.LoadRecoveryRuns(h.Session.TenantId).Single());
        var source = File.ReadAllBytes(h.Evidence.RunFile(update));
        h.Graph.MutateReadback = null; h.Graph.Mode = SessionMode.Assessment; h.Session.Mode = SessionMode.Assessment;
        h.Graph.VersionedReads.Clear();
        var record = await new WriteVerificationService(h.Evidence, h.Clock).VerifyRecoveryAsync(h.Graph, h.Session, standard, run.Id, default);
        Assert.True(record.Verified, record.Detail);
        Assert.All(h.Graph.VersionedReads, r => Assert.Equal(GraphApi.Beta, r.Api));
        Assert.Equal(created.Id, h.Evidence.LoadMappings(h.Session.TenantId).Find("EDR-TEST")!.RunId);
        Assert.Single(h.Graph.RecoveryWrites); Assert.Single(h.Graph.Writes);
        Assert.Equal(original, ToolkitJson.Serialize(h.Evidence.LoadRecoveryRuns(h.Session.TenantId).Single()));
        Assert.Equal(source, File.ReadAllBytes(h.Evidence.RunFile(update)));
        Assert.Throws<SafetyViolationException>(() => RecoverySafety.AssertPayload(standard.Collections["edr"].BasePath, plan.Action, plan.Payload, GraphApi.V1));
    }

    [Theory]
    [InlineData("CA-003", "partialClients", 1, true)]
    [InlineData("CA-003", "partialClients", 2, false)]
    [InlineData("CA-004", "exemptsMany", 5, false)]
    [InlineData("CA-004", "exemptsMany", 6, true)]
    public void Shipped_CA_array_caveats_use_counts(string control, string key, int count, bool expected)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.09.11.json"));
        var standard = StandardsLoader.Parse(File.ReadAllText(path), path);
        var caveat = standard.FindControl(control)!.Equivalence!.Caveats.Single(c => c.Key == key);
        var observed = new JsonArray(Enumerable.Range(0, count).Select(i => (JsonNode?)JsonValue.Create("synthetic-" + i)).ToArray());
        Assert.Equal(expected, EquivalenceEvaluator.Matches(caveat, observed));
    }

    [Fact]
    public async Task Ca_old_preview_cannot_bypass_material_baseline_at_execution()
    {
        using var h = CaHarness(); var source = await h.Deploy(); var plan = await Preview(h, source);
        h.Object(source)["conditions"]!["applications"]!["excludeApplications"] = new JsonArray("Office365");
        // Reproduce an older binary's preview, made after the drift, with internally consistent approval hashes.
        plan.Before = await RecoveryObjectReader.ReadAsync(h.Graph, h.Standard.Collections["conditionalAccess"], plan.ObjectId, default);
        plan.Id = Guid.NewGuid().ToString(); // A distinct historical fixture; never replace the saved preview.
        h.Evidence.SaveReviewedChangePlan(plan);
        var result = await new ReviewedChangeService(h.Evidence, h.Clock).ExecuteAsync(h.Graph, h.Session, h.Profile, h.Standard,
            plan.Id, plan.IntegrityDigest, h.Session.TenantId, default);
        Assert.Equal(WriteAcceptance.NotAttempted, result.WriteAcceptance);
        Assert.Contains("material", result.Error); Assert.Empty(h.Graph.ReviewedWrites);
        var containment = await Preview(h, source, ReviewedChangeKind.DisableConditionalAccess);
        var disabled = await new ReviewedChangeService(h.Evidence, h.Clock).ExecuteAsync(h.Graph, h.Session, h.Profile, h.Standard,
            containment.Id, containment.IntegrityDigest, h.Session.TenantId, default);
        Assert.Equal(ConfigurationVerification.Pass, disabled.Verification);
        Assert.Equal("disabled", Assert.Single(h.Graph.ReviewedWrites).Payload["state"]!.ToString());
    }

    [Fact]
    public async Task Ca_verified_readback_cannot_adopt_an_extra_material_property_already_in_the_baseline()
    {
        using var h = CaHarness();
        h.Graph.MutateReadback = o => { o["conditions"]!["applications"]!["excludeApplications"] = new JsonArray("Office365"); return o; };
        var source = await h.Deploy(); Assert.Equal(ConfigurationVerification.Pass, source.Results.Single().Configuration);
        await Assert.ThrowsAsync<SafetyViolationException>(() => Preview(h, source));
        Assert.Empty(h.Graph.ReviewedWrites);
    }

    [Fact]
    public async Task Ca_accepted_write_can_use_intact_separate_read_only_verification_as_baseline()
    {
        using var h = CaHarness(); var source = await h.Deploy();
        var item = source.Results.Single(); item.Configuration = ConfigurationVerification.Unknown; item.AfterObject = null; item.ReadbackDigest = null;
        h.Evidence.SaveRun(source); var original = File.ReadAllBytes(h.Evidence.RunFile(source));
        var record = await new WriteVerificationService(h.Evidence, h.Clock).VerifyDeploymentAsync(h.Graph, h.Session, h.Standard, source.Id, item.ControlId, false, default);
        Assert.True(record.Verified); Assert.NotNull(await Preview(h, source));
        Assert.Equal(original, File.ReadAllBytes(h.Evidence.RunFile(source)));
    }

    [Fact]
    public async Task Enrolment_service_assignment_and_removal_verify_documented_readback()
    {
        var standard = TestData.Standard();
        standard.Collections["enrolment"] = new CollectionDefinition { Api = "beta", Path = "/deviceManagement/deviceEnrollmentConfigurations",
            Scope = "DeviceManagementServiceConfig.Read.All", Write = "DeviceManagementServiceConfig.ReadWrite.All", Assignments = true };
        standard.Controls.Add(new ControlDefinition { Id = "ENR-TEST", Name = "Synthetic ESP", Collection = "enrolment",
            Assessment = new AssessmentRule { Mode = AssessmentMode.Settings }, SafeDeployment = new SafeDeployment { State = "unassigned" },
            Payload = new JsonObject { ["@odata.type"] = "#microsoft.graph.windows10EnrollmentCompletionPageConfiguration", ["displayName"] = "Synthetic ESP", ["showInstallationProgress"] = true } });
        using var h = new Harness(standard); var source = await h.Deploy("ENR-TEST");
        h.Graph.Add("/groups", new JsonObject { ["id"] = TestData.Mam, ["displayName"] = "Synthetic inclusion", ["securityEnabled"] = true });
        var service = new ReviewedChangeService(h.Evidence, h.Clock);
        foreach (var remove in new[] { false, true })
        {
            var plan = await service.PreviewAsync(h.Graph, h.Session, h.Profile, standard, await h.Capture(),
                remove ? ReviewedChangeKind.RemoveAssignments : ReviewedChangeKind.AssignGroups, "ENR-TEST", source.Results.Single().ObjectId!, remove ? [] : [TestData.Mam], [], default);
            var run = await service.ExecuteAsync(h.Graph, h.Session, h.Profile, standard, plan.Id, plan.IntegrityDigest, h.Session.TenantId, default);
            Assert.Equal(ConfigurationVerification.Pass, run.Verification);
            Assert.Equal(remove ? 0 : 1, run.After![RecoveryObjectReader.AssignmentsKey]!.AsArray().Count);
        }
        Assert.Equal(2, h.Graph.ReviewedWrites.Count);
    }

    [Theory]
    [InlineData("uninstall", "allDevicesAssignmentTarget")]
    [InlineData("available", "allDevicesAssignmentTarget")]
    [InlineData("required", "exclusionGroupAssignmentTarget")]
    [InlineData("required", "missing")]
    public void Assignment_presence_alone_never_establishes_application_compliance(string intent, string type)
    {
        var standard = TestData.Standard();
        standard.Collections["applications"] = new CollectionDefinition { Path = "/deviceAppManagement/mobileApps", Assignments = true };
        var control = new ControlDefinition { Id = "APP-TEST", Name = "Synthetic app", Collection = "applications",
            Assessment = new AssessmentRule { Mode = AssessmentMode.Settings }, ExpectedProduction = new ExpectedProduction { State = "assigned", Assignment = "All devices" },
            Payload = new JsonObject { ["displayName"] = "Synthetic app", ["@odata.type"] = "#microsoft.graph.win32LobApp", ["isFeatured"] = false } };
        standard.Controls.Add(control); var app = (JsonObject)control.Payload.DeepClone(); app["id"] = "synthetic-app";
        var row = new JsonObject { ["intent"] = intent };
        if (type != "missing") row["target"] = new JsonObject { ["@odata.type"] = "#microsoft.graph." + type };
        app[TenantCollector.AssignmentsKey] = new JsonArray(row);
        var snapshot = TestData.Snapshot(standard); snapshot.Collections["applications"].Items.Add(app);
        var finding = new AssessmentEngine(new FixedClock(), "test").Assess(snapshot, standard, TestData.Profile(), TestData.Mappings(), [], "test").Findings.Single(f => f.ControlId == control.Id);
        Assert.True(Assert.Single(finding.Candidates).SettingsMatch);
        Assert.NotEqual(FindingStatus.Compliant, finding.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Historical_enrolment_envelope_is_read_only_and_never_resolves_unknown_writes(bool unknown)
    {
        var standard = TestData.Standard();
        const string root = "/deviceManagement/deviceEnrollmentConfigurations";
        standard.Collections["enrolment"] = new CollectionDefinition { Api = "beta", Path = root, Assignments = true };
        using var h = new Harness(standard);
        var assignments = new JsonArray(new JsonObject { ["@odata.type"] = "#microsoft.graph.enrollmentConfigurationAssignment",
            ["target"] = new JsonObject { ["@odata.type"] = "#microsoft.graph.groupAssignmentTarget", ["groupId"] = TestData.Mam } });
        h.Graph.Add(root, new JsonObject { ["id"] = TestData.Office, ["_assignments"] = assignments.DeepClone() });
        var plan = new ReviewedChangePlan { TenantId = TestData.TenantA, Collection = "enrolment", ControlId = "ENR-TEST", ObjectId = TestData.Office,
            StandardDigest = CanonicalJson.Sha256Value(standard), Kind = ReviewedChangeKind.AssignGroups, Path = root + "/" + TestData.Office + "/assign",
            Api = GraphApi.Beta, Method = "POST", IncludeGroups = [TestData.Mam], Payload = new JsonObject { ["assignments"] = assignments } };
        h.Evidence.SaveReviewedChangePlan(plan);
        var run = new ReviewedChangeRun { Id = plan.Id, TenantId = plan.TenantId, ControlId = plan.ControlId, PlanDigest = plan.IntegrityDigest,
            WriteAcceptance = unknown ? WriteAcceptance.Unknown : WriteAcceptance.Accepted, Verification = ConfigurationVerification.Unknown, Status = RunStatus.ReviewRequired };
        h.Evidence.SaveReviewedChangeRun(run);
        var originalPlan = ToolkitJson.Serialize(h.Evidence.RequireReviewedChangePlan(plan.TenantId, plan.Id));
        var originalRun = ToolkitJson.Serialize(h.Evidence.LoadReviewedChangeRuns(plan.TenantId).Single());
        h.Session.Mode = SessionMode.Assessment; h.Graph.Mode = SessionMode.Assessment;
        var service = new ReviewedChangeService(h.Evidence, h.Clock);
        if (unknown) await Assert.ThrowsAsync<SafetyViolationException>(() => service.ReverifyAsync(h.Graph, h.Session, standard, run.Id, default));
        else Assert.True((await service.ReverifyAsync(h.Graph, h.Session, standard, run.Id, default)).Verified);
        Assert.Empty(h.Graph.Writes); Assert.Empty(h.Graph.ReviewedWrites);
        Assert.Equal(originalPlan, ToolkitJson.Serialize(h.Evidence.RequireReviewedChangePlan(plan.TenantId, plan.Id)));
        Assert.Equal(originalRun, ToolkitJson.Serialize(h.Evidence.LoadReviewedChangeRuns(plan.TenantId).Single()));
    }
}
