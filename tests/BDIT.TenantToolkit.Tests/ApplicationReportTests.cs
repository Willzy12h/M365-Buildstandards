using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

internal sealed class ApplicationReportFixture : IDisposable
{
    internal readonly TempRoot Root = new();
    internal readonly FixedClock Clock = new();
    internal readonly TenantSession Session = TestData.Session(mode: SessionMode.Assessment);
    internal readonly ReadTokens Tokens = new();
    internal readonly ReadHttp Http = new();
    internal readonly RecordedLog Log = new();
    private readonly HttpClient _http;
    internal readonly GraphClient Graph;
    internal ApplicationReportFixture()
    {
        Session.Scopes = [ApplicationReportRegistry.ApplicationScope, ApplicationReportRegistry.DirectoryScope];
        Session.TokenExpiresAt = Timestamps.Format(Clock.UtcNow.AddDays(10));
        _http = new HttpClient(Http);
        Graph = new GraphClient(_http, Tokens, Session.TenantId, Session.Mode, GraphRouteAllowList.FromStandard(TestData.Standard()),
            new GraphClientOptions { Sleep = false, MaxReadAttempts = 2 }, Log);
    }
    internal Task<ApplicationReportEvidence> Expiry(int horizon = 30, CancellationToken ct = default) => new ApplicationReportService(Clock).CaptureExpiryAsync(
        Graph.ForApplicationReports(2, "application-expiry"), Session, new() { HorizonDays = horizon }, ct);
    internal Task<ApplicationReportEvidence> Consent(CancellationToken ct = default) => new ApplicationReportService(Clock).CaptureConsentAsync(
        Graph.ForApplicationReports(2, "application-consent"), Session, new(), ct);
    internal static JsonObject Object(string id = TestData.Operator, string name = "Same synthetic name", JsonArray? passwords = null, JsonArray? keys = null) => new()
    { ["id"] = id, ["appId"] = TestData.ClientId, ["displayName"] = name, ["passwordCredentials"] = passwords ?? [], ["keyCredentials"] = keys ?? [] };
    internal static JsonObject Principal(string id, JsonArray? roles = null) => new()
    { ["id"] = id, ["appId"] = TestData.ClientId, ["displayName"] = "Same synthetic name", ["appRoles"] = roles?.DeepClone() ?? new JsonArray() };
    internal static JsonObject Credential(string end, string start = "2026-01-01T00:00:00Z", string? id = TestData.Office) => new()
    { ["keyId"] = id, ["displayName"] = "Synthetic metadata", ["startDateTime"] = start, ["endDateTime"] = end };
    internal static JsonObject Role(string id = TestData.Office, string name = "Read synthetic records", string value = "Synthetic.Read") => new()
    { ["id"] = id, ["displayName"] = name, ["value"] = value, ["isEnabled"] = true };
    internal static JsonObject Grant(string id = "grant-opaque_ABC-123") => new()
    { ["id"] = id, ["clientId"] = TestData.Operator, ["resourceId"] = TestData.Mam, ["consentType"] = "AllPrincipals", ["principalId"] = null, ["scope"] = "Synthetic.Read Synthetic.Write" };
    internal static JsonObject Assignment(string id = "nDJ_opaque-Base64url_Assignment-1", string role = TestData.Office) => new()
    { ["id"] = id, ["principalId"] = TestData.Operator, ["resourceId"] = TestData.Mam, ["appRoleId"] = role };
    internal static string Id(int i) => new Guid(i, 0, 0, new byte[8]).ToString("D");
    internal void ConsentData()
    {
        Http.Response = (r, _) => r.RequestUri!.AbsolutePath switch
        {
            "/v1.0/servicePrincipals" => ReadHttp.Page([Principal(TestData.Operator), Principal(TestData.Mam, [Role()])]),
            "/v1.0/oauth2PermissionGrants" => ReadHttp.Page([Grant()]),
            "/v1.0/servicePrincipals/" + TestData.Operator + "/appRoleAssignments" => ReadHttp.Page([Assignment()]),
            _ => ReadHttp.Page([])
        };
    }
    public void Dispose() { _http.Dispose(); Root.Dispose(); }
    internal sealed class RecordedLog : IToolkitLog
    {
        internal readonly List<string> Messages = [];
        public void Log(LogLevel level, string category, string message, string? tenantId = null, string? controlId = null, Exception? exception = null)
        { Messages.Add(message); if (exception is not null) Messages.Add(exception.Message); }
    }
    internal sealed class ReadTokens : IAccessTokenProvider
    {
        internal int Calls;
        public Task<string> GetAccessTokenAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); Calls++; return Task.FromResult("synthetic-token"); }
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => GetAccessTokenAsync(ct);
    }
    internal sealed class ReadHttp : HttpMessageHandler
    {
        internal readonly List<string> Routes = [];
        internal Func<HttpRequestMessage, int, HttpResponseMessage>? Response;
        internal Action<HttpRequestMessage, int>? BeforeResponse;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, request.Method); Routes.Add(request.RequestUri!.AbsoluteUri); BeforeResponse?.Invoke(request, Routes.Count); ct.ThrowIfCancellationRequested();
            return Task.FromResult(Response?.Invoke(request, Routes.Count) ?? Page([]));
        }
        internal static HttpResponseMessage Page(IEnumerable<JsonObject> objects, string? next = null)
        {
            var node = new JsonObject { ["value"] = new JsonArray(objects.Select(o => (JsonNode)o.DeepClone()).ToArray()) };
            if (next is not null) node["@odata.nextLink"] = next;
            return new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };
        }
        internal static HttpResponseMessage Error() => new(HttpStatusCode.Forbidden) { Content = new StringContent("{\"error\":{\"code\":\"denied\",\"message\":\"synthetic private secretText must not be copied\"}}") };
    }
}

public sealed class ApplicationReportTests
{
    [Theory]
    [InlineData("expiry")]
    [InlineData("consent")]
    public async Task Successful_zero_is_distinct_from_failed_unknown_and_missing_permission(string report)
    {
        using var f = new ApplicationReportFixture();
        var empty = report == "expiry" ? await f.Expiry() : await f.Consent();
        Assert.Equal(ReportReadState.Collected, empty.Status); Assert.All(empty.Sections, s => Assert.Empty(s.Rows));
        Assert.All(empty.Sources, s => Assert.Equal(ReportReadState.Collected, s.Status));
        f.Http.Response = (_, _) => ApplicationReportFixture.ReadHttp.Error();
        var failed = report == "expiry" ? await f.Expiry() : await f.Consent();
        Assert.Equal(ReportReadState.Failed, failed.Status); Assert.All(failed.Sections, s => Assert.NotEqual(ReportReadState.Collected, s.Status));
        Assert.DoesNotContain("secretText must not", ApplicationReportEvidenceSchema.Serialise(failed));
        f.Session.Scopes.Clear(); f.Http.Routes.Clear(); f.Tokens.Calls = 0;
        var missing = report == "expiry" ? await f.Expiry() : await f.Consent();
        Assert.Equal(ReportReadState.NotAttempted, missing.Status); Assert.Empty(f.Http.Routes); Assert.Equal(0, f.Tokens.Calls);
        Assert.All(missing.Sources, s => { Assert.Equal(ReportReadState.NotAttempted, s.Status); Assert.NotNull(s.Error); });
    }
    [Fact]
    public async Task Applications_and_enterprise_objects_with_same_name_and_app_id_remain_distinct()
    {
        using var f = new ApplicationReportFixture();
        f.Http.Response = (r, _) => r.RequestUri!.AbsolutePath switch
        {
            "/v1.0/applications" => ApplicationReportFixture.ReadHttp.Page([ApplicationReportFixture.Object()]),
            "/v1.0/servicePrincipals" => ApplicationReportFixture.ReadHttp.Page([ApplicationReportFixture.Object(TestData.Mam)]),
            _ => ApplicationReportFixture.ReadHttp.Page([])
        };
        var result = await f.Expiry(); Assert.Equal(ReportReadState.Collected, result.Status);
        var app = ApplicationReportEvidenceSchema.ReadRow<ApplicationExpiryRow>(Assert.Single(result.Sections[0].Rows));
        var sp = ApplicationReportEvidenceSchema.ReadRow<ApplicationExpiryRow>(Assert.Single(result.Sections[1].Rows));
        Assert.Equal(app.Name, sp.Name); Assert.Equal(app.ApplicationId, sp.ApplicationId); Assert.NotEqual(app.Id, sp.Id);
        Assert.Equal("application", app.ObjectKind); Assert.Equal("servicePrincipal", sp.ObjectKind);
        Assert.Contains(f.Http.Routes, r => r.Contains("/applications/" + app.Id + "/owners", StringComparison.Ordinal));
        Assert.Contains(f.Http.Routes, r => r.Contains("/servicePrincipals/" + sp.Id + "/owners", StringComparison.Ordinal));
    }
    [Theory]
    [InlineData("2026-09-11T10:00:00Z", "2026-01-01T00:00:00Z", "Expired")]
    [InlineData("2026-10-11T10:00:00Z", "2026-01-01T00:00:00Z", "ExpiringWithinHorizon")]
    [InlineData("2026-10-11T10:00:01Z", "2026-01-01T00:00:00Z", "ValidBeyondHorizon")]
    [InlineData("2026-10-12T00:00:00Z", "2026-09-12T00:00:00Z", "NotYetValid")]
    [InlineData("bad-date", "2026-01-01T00:00:00Z", "Unknown")]
    [InlineData("2025-01-01T00:00:00Z", "2026-01-01T00:00:00Z", "Unknown")]
    public async Task Classification_uses_fixed_end_and_horizon_including_exact_boundaries(string end, string start, string expected)
    {
        using var f = new ApplicationReportFixture();
        f.Http.Response = (r, _) => ApplicationReportFixture.ReadHttp.Page(r.RequestUri!.AbsolutePath == "/v1.0/applications"
            ? [ApplicationReportFixture.Object(passwords: [ApplicationReportFixture.Credential(end, start)])] : []);
        var result = await f.Expiry(); var row = ApplicationReportEvidenceSchema.ReadRow<ApplicationExpiryRow>(Assert.Single(result.Sections[0].Rows));
        Assert.Equal(expected, Assert.Single(row.Credentials).Classification);
        Assert.Equal(30, ((ApplicationExpiryEvidence)result).Parameters.HorizonDays);
        Assert.Equal(Timestamps.Format(f.Clock.UtcNow), result.EndedAt);
        Assert.Equal(expected == "Unknown" ? ReportReadState.Partial : ReportReadState.Collected, row.CredentialsReadStatus);
    }
    [Fact]
    public async Task End_clock_is_pinned_after_reads_and_caller_horizon_cannot_change_it()
    {
        using var f = new ApplicationReportFixture(); var parameters = new ApplicationExpiryParameters { HorizonDays = 1 };
        f.Http.BeforeResponse = (_, call) => { if (call == 2) { f.Clock.UtcNow = f.Clock.UtcNow.AddDays(1); parameters.HorizonDays = 365; } };
        f.Http.Response = (r, _) => ApplicationReportFixture.ReadHttp.Page(r.RequestUri!.AbsolutePath == "/v1.0/applications"
            ? [ApplicationReportFixture.Object(passwords: [ApplicationReportFixture.Credential("2026-09-13T10:00:00Z")])] : []);
        var report = (ApplicationExpiryEvidence)await new ApplicationReportService(f.Clock).CaptureExpiryAsync(f.Graph.ForApplicationReports(2, "application-expiry"), f.Session, parameters, default);
        Assert.Equal(1, report.Parameters.HorizonDays); Assert.Equal("2026-09-12T10:00:00.000Z", report.EndedAt);
        Assert.Equal("ExpiringWithinHorizon", report.Sections[0].Rows[0]["credentials"]![0]!["classification"]!.ToString());
    }
    [Theory]
    [InlineData("missing-password-list")]
    [InlineData("missing-key-id")]
    [InlineData("malformed-credential")]
    [InlineData("limited-owner")]
    public async Task Unreadable_lists_and_limited_owner_information_are_explicit_partial(string fault)
    {
        using var f = new ApplicationReportFixture(); var item = ApplicationReportFixture.Object();
        if (fault == "missing-password-list") item.Remove("passwordCredentials");
        if (fault == "missing-key-id") item["passwordCredentials"] = new JsonArray(ApplicationReportFixture.Credential("2026-10-01T00:00:00Z", id: null));
        if (fault == "malformed-credential") item["passwordCredentials"] = new JsonArray("invalid shape");
        f.Http.Response = (r, _) => r.RequestUri!.AbsolutePath == "/v1.0/applications" ? ApplicationReportFixture.ReadHttp.Page([item])
            : r.RequestUri.AbsolutePath.EndsWith("/owners", StringComparison.Ordinal) && fault == "limited-owner" ? ApplicationReportFixture.ReadHttp.Page([new() { ["id"] = TestData.Emergency }])
            : ApplicationReportFixture.ReadHttp.Page([]);
        var report = await f.Expiry(); Assert.Equal(ReportReadState.Partial, report.Status);
        var row = ApplicationReportEvidenceSchema.ReadRow<ApplicationExpiryRow>(Assert.Single(report.Sections[0].Rows));
        Assert.Equal(ReportReadState.Partial, row.ReadStatus); Assert.NotNull(row.Error);
        if (fault == "limited-owner") { Assert.Equal(ReportReadState.Partial, row.OwnersReadStatus); Assert.Null(Assert.Single(row.Owners).Name); }
        else Assert.Equal(ReportReadState.Partial, row.CredentialsReadStatus);
    }
    [Fact]
    public async Task Consent_observes_exact_non_guid_assignment_and_grant_ids_and_exact_resource_roles()
    {
        using var f = new ApplicationReportFixture(); f.ConsentData(); var result = await f.Consent();
        Assert.Equal(ReportReadState.Collected, result.Status);
        var grant = ApplicationReportEvidenceSchema.ReadRow<DelegatedGrantReportRow>(Assert.Single(result.Sections[1].Rows));
        var assignment = ApplicationReportEvidenceSchema.ReadRow<ApplicationRoleAssignmentReportRow>(Assert.Single(result.Sections[2].Rows));
        Assert.Equal("grant-opaque_ABC-123", grant.Id); Assert.Equal("nDJ_opaque-Base64url_Assignment-1", assignment.Id);
        Assert.Equal(TestData.Operator, grant.ClientId); Assert.Equal(TestData.Mam, assignment.ResourceId);
        Assert.Equal("Synthetic.Read Synthetic.Write", grant.Scope); Assert.Equal("Read synthetic records", assignment.RoleName);
        Assert.Equal(TestData.Office, assignment.AppRoleId); Assert.Equal(TestData.Operator, assignment.QueriedPrincipalId);
    }
    [Fact]
    public async Task Missing_directory_read_permission_skips_grants_but_still_reads_acknowledged_application_metadata()
    {
        using var f = new ApplicationReportFixture(); f.ConsentData(); f.Session.Scopes.Remove(ApplicationReportRegistry.DirectoryScope);
        var report = await f.Consent(); Assert.Equal(ReportReadState.Partial, report.Status);
        Assert.Equal(ReportReadState.NotAttempted, report.Sections[1].Status); Assert.Contains("Directory.Read.All", report.Sections[1].Error);
        Assert.DoesNotContain(f.Http.Routes, r => r.Contains("oauth2PermissionGrants", StringComparison.Ordinal));
        Assert.Single(report.Sections[2].Rows); Assert.Equal(ReportReadState.Collected, report.Sections[2].Status);
    }
    [Theory]
    [InlineData("zero-role")]
    [InlineData("wrong-resource")]
    [InlineData("wrong-principal")]
    [InlineData("duplicate-role")]
    public async Task Unknown_or_ambiguous_relationships_never_join_by_same_display_name(string fault)
    {
        using var f = new ApplicationReportFixture(); var assignment = ApplicationReportFixture.Assignment(); var roles = new JsonArray(ApplicationReportFixture.Role());
        if (fault == "zero-role") assignment["appRoleId"] = Guid.Empty.ToString();
        if (fault == "wrong-resource") assignment["resourceId"] = TestData.Operator;
        if (fault == "wrong-principal") assignment["principalId"] = TestData.Mam;
        if (fault == "duplicate-role") roles.Add(ApplicationReportFixture.Role());
        f.Http.Response = (r, _) => r.RequestUri!.AbsolutePath switch
        {
            "/v1.0/servicePrincipals" => ApplicationReportFixture.ReadHttp.Page([ApplicationReportFixture.Principal(TestData.Operator), ApplicationReportFixture.Principal(TestData.Mam, roles)]),
            "/v1.0/servicePrincipals/" + TestData.Operator + "/appRoleAssignments" => ApplicationReportFixture.ReadHttp.Page([assignment]),
            _ => ApplicationReportFixture.ReadHttp.Page([])
        };
        var result = await f.Consent(); var row = ApplicationReportEvidenceSchema.ReadRow<ApplicationRoleAssignmentReportRow>(Assert.Single(result.Sections[2].Rows));
        Assert.Equal(ReportReadState.Partial, row.ReadStatus); Assert.Equal("Unresolved", row.ResolutionStatus); Assert.NotNull(row.ResolutionReason);
        if (fault is not "wrong-principal") Assert.Null(row.RoleName);
        if (fault == "zero-role") Assert.Equal(Guid.Empty.ToString(), row.AppRoleId);
    }
    [Theory]
    [InlineData("expiry", false)]
    [InlineData("expiry", true)]
    [InlineData("consent", false)]
    [InlineData("consent", true)]
    public async Task Stable_connection_timestamp_allows_provider_read_after_initial_token_expiry(string report, bool crossesDuringRead)
    {
        using var f = new ApplicationReportFixture();
        f.Session.TokenExpiresAt = Timestamps.Format(f.Clock.UtcNow.AddSeconds(crossesDuringRead ? 1 : -1));
        var pinnedTimestamp = f.Session.TokenExpiresAt;
        if (crossesDuringRead) f.Http.BeforeResponse = (_, _) => f.Clock.UtcNow = f.Clock.UtcNow.AddSeconds(2);
        var result = report == "expiry" ? await f.Expiry() : await f.Consent();
        Assert.Equal(ReportReadState.Collected, result.Status);
        Assert.True(f.Tokens.Calls > 0); Assert.NotEmpty(f.Http.Routes);
        Assert.Equal(pinnedTimestamp, f.Session.TokenExpiresAt);
        Assert.Equal(TestData.TenantA, result.TenantId); Assert.Equal(f.Session.AccountObjectId, result.AccountObjectId);
    }
    [Theory]
    [InlineData("timestamp")]
    [InlineData("operator")]
    [InlineData("account")]
    public async Task Invalid_context_is_refused_before_token_or_http(string invalid)
    {
        using var f = new ApplicationReportFixture();
        if (invalid == "timestamp") f.Session.TokenExpiresAt = "invalid";
        else if (invalid == "operator") f.Session.OperatorVerified = false;
        else f.Session.AccountObjectId = "";
        await Assert.ThrowsAsync<TenantMismatchException>(() => f.Expiry());
        Assert.Empty(f.Http.Routes); Assert.Equal(0, f.Tokens.Calls);
    }
    [Theory]
    [InlineData("account")]
    [InlineData("scope")]
    [InlineData("expired")]
    public async Task Context_change_during_response_refuses_acceptance_without_relabel_or_save(string change)
    {
        using var f = new ApplicationReportFixture();
        f.Http.BeforeResponse = (_, _) => { if (change == "account") f.Session.AccountObjectId = TestData.Mam; else if (change == "scope") f.Session.Scopes.Add("New.Unrequested.Scope"); else f.Session.TokenExpiresAt = Timestamps.Format(f.Clock.UtcNow.AddSeconds(-1)); };
        await Assert.ThrowsAsync<TenantMismatchException>(() => f.Expiry()); Assert.Single(f.Http.Routes);
        Assert.False(Directory.Exists(Path.Combine(f.Root.Paths.TenantDirectory(TestData.TenantA), "report-evidence")));
    }
    [Theory]
    [InlineData("expiry")]
    [InlineData("consent")]
    public async Task Identity_change_on_final_empty_response_refuses_acceptance(string kind)
    {
        using var f = new ApplicationReportFixture();
        f.Http.BeforeResponse = (_, call) => { if (call == 2) f.Session.AccountObjectId = TestData.Mam; };
        await Assert.ThrowsAsync<TenantMismatchException>(() => kind == "expiry" ? f.Expiry() : f.Consent());
        Assert.Equal(2, f.Http.Routes.Count);
        Assert.False(Directory.Exists(Path.Combine(f.Root.Paths.TenantDirectory(TestData.TenantA), "report-evidence")));
    }
    [Theory]
    [InlineData("failure")]
    [InlineData("cancel")]
    public async Task Page_two_interruption_retains_metadata_and_marks_remaining_pages_unknown(string failure)
    {
        using var f = new ApplicationReportFixture(); using var stop = new CancellationTokenSource();
        f.Http.Response = (r, _) => r.RequestUri!.AbsolutePath == "/v1.0/applications" && r.RequestUri.Query.Contains("skiptoken", StringComparison.Ordinal)
            ? ApplicationReportFixture.ReadHttp.Error() : r.RequestUri.AbsolutePath == "/v1.0/applications"
            ? ApplicationReportFixture.ReadHttp.Page([ApplicationReportFixture.Object()], "https://graph.microsoft.com/v1.0/applications?$skiptoken=page2") : ApplicationReportFixture.ReadHttp.Page([]);
        f.Http.BeforeResponse = (r, _) => { if (failure == "cancel" && r.RequestUri!.Query.Contains("skiptoken", StringComparison.Ordinal)) stop.Cancel(); };
        var report = await f.Expiry(ct: stop.Token); Assert.Single(report.Sections[0].Rows);
        Assert.Equal(failure == "cancel" ? ReportReadState.Cancelled : ReportReadState.Partial, report.Status);
        Assert.NotNull(report.Sections[0].Error); Assert.DoesNotContain("synthetic private", ApplicationReportEvidenceSchema.Serialise(report));
    }
    [Theory]
    [InlineData("expiry")]
    [InlineData("consent")]
    public async Task Duplicate_directory_identity_is_ambiguous_and_never_drives_a_child_read(string kind)
    {
        using var f = new ApplicationReportFixture();
        f.Http.Response = (r, _) => r.RequestUri!.AbsolutePath == (kind == "expiry" ? "/v1.0/applications" : "/v1.0/servicePrincipals")
            ? ApplicationReportFixture.ReadHttp.Page(kind == "expiry"
                ? [ApplicationReportFixture.Object(), ApplicationReportFixture.Object(name: "Conflicting duplicate")]
                : [ApplicationReportFixture.Principal(TestData.Operator), ApplicationReportFixture.Principal(TestData.Operator)])
            : ApplicationReportFixture.ReadHttp.Page([]);
        var report = kind == "expiry" ? await f.Expiry() : await f.Consent();
        var row = Assert.Single(report.Sections[0].Rows);
        Assert.Equal("Ambiguous", row["identityStatus"]!.GetValue<string>());
        Assert.Equal(ReportReadState.Partial, report.Status);
        Assert.DoesNotContain(f.Http.Routes, r => r.Contains("/owners", StringComparison.Ordinal) || r.Contains("/appRoleAssignments", StringComparison.Ordinal));
        Assert.Equal(0, report.Sources[2].ObjectsAttempted);
        Assert.NotEqual(ReportReadState.Collected, report.Sources[2].Status);
    }
    [Fact]
    public async Task Duplicate_owner_retains_one_unresolved_entry_and_an_explicit_incomplete_list()
    {
        using var f = new ApplicationReportFixture();
        f.Http.Response = (r, _) => r.RequestUri!.AbsolutePath == "/v1.0/applications"
            ? ApplicationReportFixture.ReadHttp.Page([ApplicationReportFixture.Object()])
            : r.RequestUri.AbsolutePath.EndsWith("/owners", StringComparison.Ordinal)
            ? ApplicationReportFixture.ReadHttp.Page([new() { ["id"] = TestData.Emergency, ["displayName"] = "First owner" }, new() { ["id"] = TestData.Emergency, ["displayName"] = "Conflicting duplicate" }])
            : ApplicationReportFixture.ReadHttp.Page([]);
        var result = await f.Expiry(); var row = ApplicationReportEvidenceSchema.ReadRow<ApplicationExpiryRow>(Assert.Single(result.Sections[0].Rows));
        Assert.Equal(ReportReadState.Partial, result.Status); Assert.Equal(ReportReadState.Partial, row.OwnersReadStatus);
        var owner = Assert.Single(row.Owners); Assert.Equal("First owner", owner.Name); Assert.Equal(ReportReadState.Partial, owner.ReadStatus);
        Assert.Contains("Duplicate", owner.Reason); Assert.Contains("Duplicate", row.OwnerReadReason);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public async Task Invalid_read_budget_is_refused_before_token_or_http(int seconds)
    {
        using var f = new ApplicationReportFixture();
        await Assert.ThrowsAsync<ConfigurationException>(() => new ApplicationReportService(f.Clock, TimeSpan.FromSeconds(seconds))
            .CaptureExpiryAsync(f.Graph.ForApplicationReports(2, "application-expiry"), f.Session, new(), default));
        Assert.Equal(0, f.Tokens.Calls); Assert.Empty(f.Http.Routes);
    }
    [Fact]
    public async Task Child_next_link_cannot_change_principal_id_or_relation()
    {
        using var f = new ApplicationReportFixture();
        f.Http.Response = (r, _) => r.RequestUri!.AbsolutePath == "/v1.0/applications" ? ApplicationReportFixture.ReadHttp.Page([ApplicationReportFixture.Object()])
            : r.RequestUri.AbsolutePath.EndsWith("/owners", StringComparison.Ordinal) ? ApplicationReportFixture.ReadHttp.Page([new() { ["id"] = TestData.Emergency, ["displayName"] = "Known synthetic owner" }], "https://graph.microsoft.com/v1.0/applications/" + TestData.Mam + "/owners?$skiptoken=wrong-object")
            : ApplicationReportFixture.ReadHttp.Page([]);
        var report = await f.Expiry(); Assert.Equal(ReportReadState.Partial, report.Status);
        Assert.DoesNotContain(f.Http.Routes, r => r.Contains("wrong-object", StringComparison.Ordinal));
        Assert.Single(report.Sections[0].Rows[0]["owners"]!.AsArray());
    }
}
