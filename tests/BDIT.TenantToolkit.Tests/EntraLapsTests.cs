using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Prerequisites;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class EntraLapsTests
{
    internal static JsonObject Policy(bool enabled = false)
    {
        var policy = ToolkitJson.ParseObject("""
        {"id":"deviceRegistrationPolicy","userDeviceQuota":37,"multiFactorAuthConfiguration":"required",
         "azureADRegistration":{"isAdminConfigurable":false,"allowedToRegister":{"@odata.type":"#microsoft.graph.allDeviceRegistrationMembership"}},
         "azureADJoin":{"isAdminConfigurable":true,"allowedToJoin":{"@odata.type":"#microsoft.graph.enumeratedDeviceRegistrationMembership","users":[],"groups":["44444444-4444-4444-8444-444444444444"]},
             "localAdmins":{"enableGlobalAdmins":false,"registeringUsers":{"@odata.type":"#microsoft.graph.noDeviceRegistrationMembership"}}},
         "localAdminPassword":{"isEnabled":false}}
        """);
        policy["localAdminPassword"]!["isEnabled"] = enabled;
        return policy;
    }

    internal sealed class Harness : IDisposable
    {
        public TempRoot Root { get; } = new();
        public FixedClock Clock { get; } = new();
        public StandardCatalogue Standard { get; } = TestData.Standard();
        public TenantSession Session { get; set; } = TestData.Session();
        public TenantSnapshot Snapshot { get; }
        public EvidenceStore Evidence { get; }
        public Handler Http { get; } = new();
        public Tokens TokenProvider { get; } = new();
        public EntraLapsService Service { get; }
        public GraphClient Graph { get; }
        private readonly HttpClient _client;
        public Harness(bool enabled = false)
        {
            Standard.Collections["deviceRegistration"] = new CollectionDefinition { Path = EntraLapsSafety.Path, Scope = "Policy.Read.DeviceConfiguration", Write = EntraLapsSafety.WriteScope, Singleton = true, Label = "Entra LAPS" };
            Snapshot = TestData.Snapshot(Standard);
            Http.Current = Policy(enabled);
            Snapshot.Collections["deviceRegistration"].Items.Add((JsonObject)Http.Current.DeepClone());
            Snapshot.Collections["deviceRegistration"].Count = 1;
            Evidence = new(Root.Paths, NullLog.Instance); Evidence.SaveSnapshot(Snapshot);
            Service = new(Evidence, Clock);
            _client = new HttpClient(Http);
            Graph = new(_client, TokenProvider, Session.TenantId, SessionMode.Deployment, GraphRouteAllowList.FromStandard(Standard),
                new GraphClientOptions { Sleep = false, MaxReadAttempts = 1 }, NullLog.Instance);
        }
        public Task<EntraLapsPlan> Preview() => Service.PreviewAsync(Graph, Session, Standard, Snapshot, default);
        public Task<EntraLapsRun> Execute(EntraLapsPlan p) => Service.ExecuteAsync(Graph, Session, Standard, p.Id, p.IntegrityDigest, default);
        public void Dispose() { _client.Dispose(); Root.Dispose(); }
    }

    internal sealed class Tokens : IAccessTokenProvider
    {
        public bool Fail { get; set; }
        public Task<string> GetAccessTokenAsync(CancellationToken ct) => Fail ? throw new AuthenticationRequiredException("Synthetic renewal failure") : Task.FromResult("synthetic");
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => GetAccessTokenAsync(ct);
    }

    internal sealed class Handler : HttpMessageHandler
    {
        public JsonObject Current { get; set; } = Policy();
        public JsonObject? Sent { get; private set; }
        public int Puts { get; private set; }
        public int Gets { get; private set; }
        public HttpStatusCode WriteStatus { get; set; } = HttpStatusCode.OK;
        public bool FailReadback { get; set; }
        public bool IgnoreWrite { get; set; }
        public Action? OnPut { get; set; }
        public Action<int>? OnGet { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(GraphClient.Host + "/v1.0" + EntraLapsSafety.Path, request.RequestUri!.AbsoluteUri);
            if (request.Method == HttpMethod.Put)
            {
                Puts++; OnPut?.Invoke(); Sent = ToolkitJson.ParseObject(await request.Content!.ReadAsStringAsync(ct));
                if (WriteStatus != HttpStatusCode.OK) return new HttpResponseMessage(WriteStatus);
                if (!IgnoreWrite) Current = (JsonObject)Sent.DeepClone();
            }
            else
            {
                Assert.Equal(HttpMethod.Get, request.Method); Gets++; OnGet?.Invoke(Gets);
                if (Puts > 0 && FailReadback) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Current.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Full_PUT_preserves_registration_and_journals_intent_before_transport()
    {
        using var h = new Harness(); var p = await h.Preview();
        h.Http.OnPut = () => { var run = Assert.Single(h.Evidence.LoadEntraLapsRuns(TestData.TenantA)); Assert.Equal(WriteAcceptance.Unknown, run.WriteAcceptance); Assert.Null(run.EndedAt); };
        var result = await h.Execute(p);
        Assert.Equal(WriteAcceptance.Accepted, result.WriteAcceptance); Assert.Equal(ConfigurationVerification.Pass, result.Verification);
        Assert.Equal(RunStatus.Completed, result.Status); Assert.NotNull(result.EndedAt);
        Assert.True(EntraLapsSafety.SameState(EntraLapsSafety.EnablePayload(Policy()), h.Http.Sent!));
        Assert.False(EntraLapsSafety.IsEnabled(p.Before)); Assert.Equal(37, h.Http.Sent!["userDeviceQuota"]!.GetValue<int>());
        Assert.Single(h.Evidence.LoadEntraLapsRuns(TestData.TenantA));
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Execute(p)); Assert.Equal(1, h.Http.Puts);
    }

    [Fact]
    public async Task Already_enabled_is_a_verified_no_op()
    {
        using var h = new Harness(true); var p = await h.Preview(); Assert.True(p.AlreadyEnabled);
        var run = await h.Execute(p); Assert.Equal(WriteAcceptance.NotAttempted, run.WriteAcceptance);
        Assert.Equal(ConfigurationVerification.Pass, run.Verification); Assert.Equal(0, h.Http.Puts);
    }

    [Theory]
    [InlineData("userDeviceQuota")]
    [InlineData("multiFactorAuthConfiguration")]
    [InlineData("azureADJoin")]
    [InlineData("azureADRegistration")]
    [InlineData("localAdminPassword")]
    public async Task Incomplete_registration_never_sends_a_replacement(string property)
    {
        using var h = new Harness(); h.Http.Current.Remove(property);
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Preview()); Assert.Equal(0, h.Http.Puts);
    }

    [Fact]
    public async Task Changed_registration_after_preview_and_after_preflight_are_both_not_sent()
    {
        using var h = new Harness(); var p = await h.Preview();
        h.Http.OnGet = count => { if (count == 3) h.Http.Current["userDeviceQuota"] = 99; };
        var run = await h.Execute(p);
        Assert.Equal(WriteAcceptance.NotAttempted, run.WriteAcceptance); Assert.Equal(ConfigurationVerification.NotRun, run.Verification);
        Assert.Equal(0, h.Http.Puts); h.Evidence.AssertNoUnresolvedEntraLaps(TestData.TenantA);
    }

    [Theory]
    [InlineData(403, WriteAcceptance.Rejected)]
    [InlineData(408, WriteAcceptance.Unknown)]
    [InlineData(503, WriteAcceptance.Unknown)]
    public async Task Rejected_and_ambiguous_writes_are_distinct_and_never_replayed(int status, string acceptance)
    {
        using var h = new Harness(); var p = await h.Preview(); h.Http.WriteStatus = (HttpStatusCode)status;
        var run = await h.Execute(p); Assert.Equal(acceptance, run.WriteAcceptance); Assert.Equal(RunStatus.ReviewRequired, run.Status);
        Assert.Equal(1, h.Http.Puts); await Assert.ThrowsAsync<SafetyViolationException>(() => h.Execute(p));
        if (acceptance == WriteAcceptance.Unknown) await Assert.ThrowsAsync<SafetyViolationException>(() => h.Preview());
        else Assert.NotNull(await h.Preview());
    }

    [Fact]
    public async Task Token_failure_before_dispatch_does_not_lock_the_prerequisite()
    {
        using var h = new Harness(); var p = await h.Preview();
        h.Http.OnGet = count => { if (count == 3) h.TokenProvider.Fail = true; };
        var run = await h.Execute(p); Assert.Equal(WriteAcceptance.NotAttempted, run.WriteAcceptance);
        Assert.Equal(0, h.Http.Puts); h.Evidence.AssertNoUnresolvedEntraLaps(TestData.TenantA);
        h.TokenProvider.Fail = false; Assert.NotNull(await h.Preview());
    }

    [Fact]
    public async Task Accepted_readback_failure_can_be_reverified_without_changing_original_evidence_or_repeating_write()
    {
        using var h = new Harness(); var p = await h.Preview(); h.Http.FailReadback = true;
        var run = await h.Execute(p); Assert.Equal(WriteAcceptance.Accepted, run.WriteAcceptance);
        Assert.Equal(ConfigurationVerification.Unknown, run.Verification);
        Assert.Throws<SafetyViolationException>(() => h.Evidence.AssertNoUnresolvedEntraLaps(TestData.TenantA));
        h.Http.FailReadback = false;
        var v = await h.Service.ReverifyAsync(h.Graph, TestData.Session(mode: SessionMode.Assessment), run.Id, default);
        Assert.Equal(ConfigurationVerification.Pass, v.Verification); Assert.Equal(1, h.Http.Puts);
        Assert.Equal(run.IntegrityDigest, h.Evidence.LoadEntraLapsRuns(TestData.TenantA).Single().IntegrityDigest);
        h.Evidence.AssertNoUnresolvedEntraLaps(TestData.TenantA);
    }

    [Fact]
    public async Task Readback_must_verify_preserved_settings_as_well_as_LAPS()
    {
        using var h = new Harness(); var p = await h.Preview();
        h.Http.OnGet = _ => { if (h.Http.Puts > 0) h.Http.Current["userDeviceQuota"] = 0; };
        var run = await h.Execute(p); Assert.True(EntraLapsSafety.IsEnabled(run.After!));
        Assert.Equal(ConfigurationVerification.Unknown, run.Verification);
    }

    [Fact]
    public async Task Approval_identity_age_and_snapshot_integrity_are_enforced()
    {
        using var h = new Harness(); var p = await h.Preview();
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Service.ExecuteAsync(h.Graph, h.Session, h.Standard, p.Id, "wrong", default));
        h.Session = TestData.Session(operatorId: TestData.Emergency);
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Execute(p));
        h.Session = TestData.Session(); h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(21);
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Execute(p));
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(-21); h.Snapshot.Collections["deviceRegistration"].Items[0]["userDeviceQuota"] = 99; h.Evidence.SaveSnapshot(h.Snapshot);
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Execute(p)); Assert.Equal(0, h.Http.Puts);
    }

    [Fact]
    public async Task Assessment_and_generic_write_routes_cannot_enable_LAPS()
    {
        using var h = new Harness(); var p = await h.Preview(); h.Session = TestData.Session(mode: SessionMode.Assessment);
        await Assert.ThrowsAsync<WriteDeniedException>(() => h.Execute(p));
        await Assert.ThrowsAsync<WriteDeniedException>(() => h.Graph.WriteAsync(GraphApi.V1, GraphWriteMethod.Post, EntraLapsSafety.Path, p.Payload, default));
        Assert.Equal(0, h.Http.Puts);
    }

    [Fact]
    public async Task Operator_stop_after_dispatch_keeps_acceptance_and_records_incomplete_verification()
    {
        using var h = new Harness(); var p = await h.Preview(); using var stop = new CancellationTokenSource();
        h.Http.OnPut = stop.Cancel;
        var run = await h.Service.ExecuteAsync(h.Graph, h.Session, h.Standard, p.Id, p.IntegrityDigest, stop.Token);
        Assert.Equal(WriteAcceptance.Accepted, run.WriteAcceptance); Assert.Equal(ConfigurationVerification.Unknown, run.Verification);
        Assert.Equal(RunStatus.ReviewRequired, run.Status); Assert.NotNull(run.EndedAt); Assert.Equal(1, h.Http.Puts);
    }

    [Fact]
    public async Task Cancelled_preflight_and_cross_tenant_preview_never_send_a_write()
    {
        using var h = new Harness(); var p = await h.Preview(); using var stop = new CancellationTokenSource(); stop.Cancel();
        var run = await h.Service.ExecuteAsync(h.Graph, h.Session, h.Standard, p.Id, p.IntegrityDigest, stop.Token);
        Assert.Equal(WriteAcceptance.NotAttempted, run.WriteAcceptance); Assert.Equal(ConfigurationVerification.NotRun, run.Verification);
        h.Evidence.AssertNoUnresolvedEntraLaps(TestData.TenantA);
        h.Session = TestData.Session(tenant: TestData.TenantB);
        await Assert.ThrowsAsync<TenantMismatchException>(() => h.Preview()); Assert.Equal(0, h.Http.Puts);
    }

    [Fact]
    public void Nested_missing_or_future_registration_settings_fail_closed()
    {
        var policy = Policy(); policy["azureADJoin"]!["localAdmins"]!.AsObject().Remove("enableGlobalAdmins");
        Assert.Throws<SafetyViolationException>(() => EntraLapsSafety.EnablePayload(policy));
        policy = Policy(); policy["futureRequiredProperty"] = "preserve-me";
        Assert.Throws<SafetyViolationException>(() => EntraLapsSafety.EnablePayload(policy));
    }
}
