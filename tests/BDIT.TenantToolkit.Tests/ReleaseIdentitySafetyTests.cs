using System.Net;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Execution;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ReleaseIdentitySafetyTests
{
    public static IEnumerable<object[]> Kinds => Enum.GetValues<ReviewedChangeKind>().Where(ReleaseIdentityChanges.Supports).Select(k => new object[] { k });

    internal static JsonObject Before(ReviewedChangeKind kind) => ToolkitJson.ParseObject(kind switch
    {
        ReviewedChangeKind.EnablePasskeys => """{"id":"FIDO2","@odata.type":"#microsoft.graph.fido2AuthenticationMethodConfiguration","state":"disabled","includeTargets":[{"id":"all_users","targetType":"group"}],"keyRestrictions":{"isEnforced":true,"enforcementType":"allow","aaGuids":["aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"]},"isAttestationEnforced":true}""",
        ReviewedChangeKind.EnableSystemPreferredMfa => """{"id":"authenticationMethodsPolicy","systemCredentialPreferences":{"state":"disabled","includeTargets":["all_users"],"excludeTargets":[]}}""",
        ReviewedChangeKind.EnableRegistrationCampaign => """{"id":"authenticationMethodsPolicy","registrationEnforcement":{"authenticationMethodsRegistrationCampaign":{"state":"disabled","snoozeDurationInDays":7,"excludeTargets":[],"includeTargets":[]}}}""",
        ReviewedChangeKind.DisableUserConsent => """{"id":"authorizationPolicy","defaultUserRolePermissions":{"allowedToCreateApps":true,"allowedToUseSSPR":false,"permissionGrantPoliciesAssigned":["managePermissionGrantsForSelf.microsoft-user-default-low","managePermissionGrantsForOwnedResource.microsoft-dynamically-managed-permissions-for-team"]}}""",
        ReviewedChangeKind.EnableAdminConsent => """{"id":"adminConsentRequestPolicy","isEnabled":false,"notifyReviewers":true,"remindersEnabled":false,"requestDurationInDays":30,"reviewers":[]}""",
        ReviewedChangeKind.SetProvisioningOwner => $$"""{"id":"{{TestData.Office}}","displayName":"Any group name","securityEnabled":true,"mailEnabled":false,"groupTypes":[],"_owners":[]} """,
        _ => throw new InvalidOperationException()
    });

    private sealed class Harness : IDisposable
    {
        public TempRoot Root { get; } = new();
        public StandardCatalogue Standard { get; } = Release20260912Tests.Standard();
        public TenantProfile Profile { get; } = TestData.Profile();
        public TenantSession Session { get; } = TestData.Session();
        public TenantSnapshot Snapshot { get; }
        public EvidenceStore Evidence { get; }
        public ReviewedChangeService Service { get; }
        public IdentityGraph Graph { get; }
        public ReviewedChangeKind Kind { get; }
        public Harness(ReviewedChangeKind kind)
        {
            Kind = kind; Evidence = new(Root.Paths, NullLog.Instance); Service = new(Evidence, new FixedClock());
            Profile.Parameters.PolicyInputs = new() { ["adminConsentReviewerIds"] = new JsonArray(TestData.Operator) };
            Snapshot = TestData.Snapshot(Standard); Evidence.SaveSnapshot(Snapshot); Graph = new(kind);
            if (kind == ReviewedChangeKind.SetProvisioningOwner)
            {
                var mappings = TestData.Mappings(); mappings.ByControl["PRE-011"] = new ManagedObjectMapping { ControlId = "PRE-011", Collection = "groups", ObjectId = TestData.Office };
                Evidence.SaveMappings(mappings);
                Evidence.SaveRun(new DeploymentRun { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, StartedAt = Snapshot.CapturedAt, Status = RunStatus.Completed,
                    Results = new() { new RunResult { ControlId = "PRE-011", Collection = "groups", ObjectId = TestData.Office, PlannedAction = nameof(PlanAction.Create), WriteAcceptance = WriteAcceptance.Accepted } } });
            }
        }
        public Task<ReviewedChangePlan> Preview() => Service.PreviewAsync(Graph, Session, Profile, Standard, Snapshot, Kind, ReleaseIdentityChanges.Route(Kind, TestData.Office).Control,
            "", Array.Empty<string>(), Array.Empty<string>(), default);
        public Task<ReviewedChangeRun> Execute(ReviewedChangePlan p, string tenant = TestData.TenantA) => Service.ExecuteAsync(Graph, Session, Profile, Standard, p.Id, p.IntegrityDigest, tenant, default);
        public void Dispose() => Root.Dispose();
    }

    private sealed class IdentityGraph(ReviewedChangeKind kind) : IGraphClient
    {
        public string TenantId { get; set; } = TestData.TenantA;
        public SessionMode Mode { get; set; } = SessionMode.Deployment;
        public JsonObject State { get; } = Before(kind);
        public JsonObject ServicePrincipal { get; } = new() { ["id"] = TestData.Mam, ["appId"] = ReleaseIdentityChanges.ProvisioningAppId };
        public int Writes { get; private set; }
        public Action? AtWrite { get; set; }
        public bool Fail { get; set; }
        public Task<JsonObject> GetAsync(GraphApi api, string path, CancellationToken ct)
        {
            if (path.StartsWith("/users/", StringComparison.Ordinal)) return Task.FromResult(new JsonObject { ["id"] = TestData.Operator, ["accountEnabled"] = true });
            return Task.FromResult((JsonObject)State.DeepClone());
        }
        public Task<IReadOnlyList<JsonObject>> GetAllAsync(GraphApi api, string path, CancellationToken ct) => Task.FromResult<IReadOnlyList<JsonObject>>(
            path.StartsWith("/servicePrincipals", StringComparison.Ordinal) ? new[] { (JsonObject)ServicePrincipal.DeepClone() }
            : path.EndsWith("/passkeyProfiles", StringComparison.Ordinal) ? (State["passkeyProfiles"] as JsonArray ?? new()).OfType<JsonObject>().Select(o => (JsonObject)o.DeepClone()).ToArray()
            : path.Contains("/members?", StringComparison.Ordinal) ? (State["_members"] as JsonArray ?? new()).OfType<JsonObject>().Select(o => (JsonObject)o.DeepClone()).ToArray()
            : State["_owners"]!.AsArray().OfType<JsonObject>().Select(o => (JsonObject)o.DeepClone()).ToArray());
        public Task<JsonObject> WriteAsync(GraphApi api, GraphWriteMethod method, string path, JsonObject payload, CancellationToken ct) => throw new InvalidOperationException("Generic write forbidden");
        public Task ApplyReviewedChangeAsync(ReviewedChangePlan p, CancellationToken ct)
        {
            AtWrite?.Invoke(); Writes++; ReviewedChangeSafety.Assert(p);
            if (Fail) throw new AmbiguousWriteException("Synthetic network uncertainty", null);
            if (p.Kind == ReviewedChangeKind.SetProvisioningOwner) State["_owners"]!.AsArray().Add(new JsonObject { ["id"] = TestData.Mam });
            else foreach (var pair in p.Payload) State[pair.Key] = pair.Value?.DeepClone();
            return Task.CompletedTask;
        }
    }

    [Theory][MemberData(nameof(Kinds))]
    public async Task Every_new_reviewed_write_is_preview_only_until_tenant_confirmation_and_durable_intent(ReviewedChangeKind kind)
    {
        using var h = new Harness(kind); var plan = await h.Preview(); Assert.Equal(0, h.Graph.Writes);
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Execute(plan, TestData.TenantB)); Assert.Equal(0, h.Graph.Writes);
        h.Graph.AtWrite = () =>
        {
            var stored = Assert.Single(h.Evidence.LoadReviewedChangeRuns(TestData.TenantA));
            Assert.Equal(WriteAcceptance.Unknown, stored.WriteAcceptance); Assert.Equal(plan.IntegrityDigest, stored.PlanDigest);
            Assert.NotNull(h.Evidence.LoadSnapshot(TestData.TenantA, h.Snapshot.Id));
        };
        var run = await h.Execute(plan); Assert.Equal(1, h.Graph.Writes); Assert.Equal(ConfigurationVerification.Pass, run.Verification); Assert.NotNull(run.After);
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Execute(plan)); Assert.Equal(1, h.Graph.Writes);
    }

    [Theory][MemberData(nameof(Kinds))]
    public async Task Unknown_write_is_attempted_once_and_blocks_further_preview(ReviewedChangeKind kind)
    {
        using var h = new Harness(kind); var plan = await h.Preview(); h.Graph.Fail = true;
        var run = await h.Execute(plan); Assert.Equal(WriteAcceptance.Unknown, run.WriteAcceptance); Assert.Equal(1, h.Graph.Writes);
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Preview()); Assert.Equal(1, h.Graph.Writes);
    }

    [Theory][MemberData(nameof(Kinds))]
    public async Task Read_only_session_missing_snapshot_and_drift_refuse_writes(ReviewedChangeKind kind)
    {
        using var h = new Harness(kind); var plan = await h.Preview(); h.Graph.Mode = SessionMode.Assessment;
        await Assert.ThrowsAsync<WriteDeniedException>(() => h.Execute(plan)); h.Graph.Mode = SessionMode.Deployment;
        h.Graph.State["changedAfterPreview"] = true;
        var drift = await h.Execute(plan); Assert.Equal(WriteAcceptance.NotAttempted, drift.WriteAcceptance); Assert.Equal(0, h.Graph.Writes);
        using var other = new Harness(kind); other.Snapshot.Complete = false; other.Evidence.SaveSnapshot(other.Snapshot);
        await Assert.ThrowsAsync<PlanValidationException>(() => other.Preview()); Assert.Equal(0, other.Graph.Writes);
    }

    [Theory][MemberData(nameof(Kinds))]
    public async Task Payload_route_permission_and_method_tampering_are_rejected(ReviewedChangeKind kind)
    {
        using var h = new Harness(kind); var plan = await h.Preview();
        foreach (var mutate in new Action<ReviewedChangePlan>[] { p => p.Payload["unexpected"] = true, p => p.Path += "/other", p => p.Method = "DELETE", p => p.RequiredScope = "Directory.ReadWrite.All" })
        {
            var copy = ToolkitJson.Deserialize<ReviewedChangePlan>(ToolkitJson.Serialize(plan)); mutate(copy);
            Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(copy));
        }
        plan.Before = new JsonObject(); Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));
    }

    [Fact]
    public async Task Identity_payload_values_preserve_other_settings_and_require_reviewers_and_exact_owner_identity()
    {
        using var consent = new Harness(ReviewedChangeKind.DisableUserConsent); var off = await consent.Preview();
        Assert.Single(off.Payload["defaultUserRolePermissions"]!["permissionGrantPoliciesAssigned"]!.AsArray());
        Assert.StartsWith("managePermissionGrantsForOwnedResource.", off.Payload["defaultUserRolePermissions"]!["permissionGrantPoliciesAssigned"]![0]!.ToString());
        Assert.False(off.Payload["defaultUserRolePermissions"]!["allowedToUseSSPR"]!.GetValue<bool>());
        using var campaign = new Harness(ReviewedChangeKind.EnableRegistrationCampaign); var registration = await campaign.Preview();
        var c = registration.Payload["registrationEnforcement"]!["authenticationMethodsRegistrationCampaign"]!;
        Assert.Equal("enabled", c["state"]!.ToString()); Assert.Equal(7, c["snoozeDurationInDays"]!.GetValue<int>());
        Assert.Equal("microsoftAuthenticator", c["includeTargets"]![0]!["targetedAuthenticationMethod"]!.ToString());
        using var passkeys = new Harness(ReviewedChangeKind.EnablePasskeys); var keys = await passkeys.Preview();
        Assert.Equal(3, keys.Payload["keyRestrictions"]!["aaGuids"]!.AsArray().Count); Assert.Null(keys.Payload["isAttestationEnforced"]);
        passkeys.Graph.State["passkeyProfiles"] = new JsonArray(new JsonObject { ["id"] = "new-model" });
        await Assert.ThrowsAsync<SafetyViolationException>(() => passkeys.Preview());
        using var reviewers = new Harness(ReviewedChangeKind.EnableAdminConsent); reviewers.Profile.Parameters.PolicyInputs!.Clear();
        await Assert.ThrowsAsync<ConfigurationException>(() => reviewers.Preview());
        using var owner = new Harness(ReviewedChangeKind.SetProvisioningOwner); owner.Graph.ServicePrincipal["appId"] = TestData.ClientId;
        await Assert.ThrowsAsync<SafetyViolationException>(() => owner.Preview());
        owner.Graph.ServicePrincipal["appId"] = ReleaseIdentityChanges.ProvisioningAppId; owner.Evidence.SaveMappings(TestData.Mappings());
        await Assert.ThrowsAsync<SafetyViolationException>(() => owner.Preview()); Assert.Equal(0, owner.Graph.Writes);
    }

    [Theory][MemberData(nameof(Kinds))]
    public async Task A_failure_to_persist_the_run_blocks_every_new_write(ReviewedChangeKind kind)
    {
        using var h = new Harness(kind); var plan = await h.Preview();
        Directory.CreateDirectory(Path.Combine(h.Evidence.TenantDirectory(TestData.TenantA), "reviewed-changes", "run-" + plan.Id + ".json"));
        await Assert.ThrowsAnyAsync<Exception>(() => h.Execute(plan)); Assert.Equal(0, h.Graph.Writes);
    }

    [Fact]
    public async Task Adding_the_provisioning_owner_requires_the_recorded_group_to_still_be_empty()
    {
        using var h = new Harness(ReviewedChangeKind.SetProvisioningOwner);
        h.Graph.State["_members"] = new JsonArray(new JsonObject { ["id"] = TestData.Operator });
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Preview()); Assert.Equal(0, h.Graph.Writes);
    }

    [Theory][MemberData(nameof(Kinds))]
    public async Task Every_new_transport_uses_its_declared_method_and_never_retries_service_unavailable(ReviewedChangeKind kind)
    {
        using var h = new Harness(kind); var plan = await h.Preview(); var handler = new Handler(plan.Method);
        using var http = new HttpClient(handler); var graph = new GraphClient(http, new Tokens(), TestData.TenantA, SessionMode.Deployment,
            GraphRouteAllowList.FromStandard(h.Standard), new GraphClientOptions { Sleep = false }, NullLog.Instance);
        await Assert.ThrowsAsync<AmbiguousWriteException>(() => graph.ApplyReviewedChangeAsync(plan, default)); Assert.Equal(1, handler.Count);
    }
    private sealed class Handler(string method) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Count++; Assert.Equal(method, request.Method.Method); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }
    }
    private sealed class Tokens : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult("synthetic");
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => GetAccessTokenAsync(ct);
    }
}
