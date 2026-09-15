using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Graph.Auth;
using BDIT.TenantToolkit.Graph.Setup;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ApplicationSetupTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Operator = "22222222-2222-2222-2222-222222222222";
    private const string GraphSp = "33333333-3333-3333-3333-333333333333";
    private sealed class Tokens : IAccessTokenProvider
    {
        public bool Fail { get; set; }
        public Task<string> GetAccessTokenAsync(CancellationToken ct) => Fail
            ? throw new AuthenticationRequiredException("Synthetic renewal failure") : Task.FromResult("synthetic-token");
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => GetAccessTokenAsync(ct);
    }

    private sealed class Fixture : IDisposable
    {
        public StandardCatalogue Standard { get; } = TestData.Standard();
        public FakeGraph Handler { get; }
        public Tokens TokenProvider { get; } = new();
        public ApplicationSetupService Service { get; }
        public string Evidence { get; } = Path.Combine(Path.GetTempPath(), "toolkit-setup-tests-" + Guid.NewGuid().ToString("N"));
        public Fixture()
        {
            Handler = new FakeGraph(Standard);
            Service = new ApplicationSetupService(new HttpClient(Handler), TokenProvider, new SignInOutcome
            { TenantId = Tenant, AccountObjectId = Operator, Account = "engineer@example.invalid" }, NullLog.Instance);
        }
        public Task<ApplicationSetupPlan> Preview() => Service.PreviewAsync(Standard, "Test", CancellationToken.None);
        public Task<ApplicationSetupResult> Execute(ApplicationSetupPlan plan, CancellationToken ct = default) => Service.ExecuteAsync(plan, Standard, Tenant, true, Evidence, ct);
        public void Dispose() { if (Directory.Exists(Evidence)) Directory.Delete(Evidence, recursive: true); }
    }

    private sealed class FakeGraph : HttpMessageHandler
    {
        public List<JsonObject> Applications { get; } = new();
        public List<JsonObject> Principals { get; } = new();
        public List<JsonObject> Grants { get; } = new();
        public List<JsonObject> Assignments { get; } = new();
        public JsonObject Resource { get; }
        public List<string> Writes { get; } = new();
        public bool WrongTenant { get; set; }
        public bool WrongOperator { get; set; }
        public bool FailFirstWriteAmbiguously { get; set; }
        public bool TimeoutStatusOnWrite { get; set; }
        public int FailWriteNumber { get; set; }
        public bool IgnorePrincipalHomepage { get; set; }
        public Action<CancellationToken>? OnWrite { get; set; }
        private int _next = 10;
        public FakeGraph(StandardCatalogue standard)
        {
            Resource = new JsonObject
            {
                ["id"] = GraphSp, ["appId"] = ApplicationSetupService.GraphApplicationId,
                ["oauth2PermissionScopes"] = new JsonArray(ApplicationSetupService.RequiredScopes(standard, SessionMode.Deployment)
                    .Select((scope, i) => (JsonNode)new JsonObject
                    {
                        ["id"] = Id(i + 100), ["value"] = scope, ["isEnabled"] = true,
                        ["type"] = "Admin", ["adminConsentDescription"] = "Synthetic " + scope
                    }).ToArray())
            };
        }
        private static string Id(int n) => "aaaaaaaa-aaaa-aaaa-aaaa-" + n.ToString("D12");
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath["/v1.0".Length..];
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            if (request.Method != HttpMethod.Get)
            {
                Writes.Add(path);
                OnWrite?.Invoke(ct);
                if (FailFirstWriteAmbiguously || Writes.Count == FailWriteNumber) throw new HttpRequestException("Synthetic disconnected response");
                if (TimeoutStatusOnWrite) return Json(new JsonObject(), HttpStatusCode.RequestTimeout);
                if (request.Method == HttpMethod.Put)
                {
                    Assert.EndsWith("/logo", path);
                    Assert.Equal("image/png", request.Content!.Headers.ContentType!.MediaType);
                    Assert.Equal(ProductLogo.Read(), await request.Content.ReadAsByteArrayAsync(ct));
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                var item = ToolkitJson.ParseObject(await request.Content!.ReadAsStringAsync(ct));
                if (request.Method == HttpMethod.Patch)
                {
                    var collection = path.StartsWith("/applications/") ? Applications : Principals;
                    var target = collection.Single(a => Value(a, "id") == path.Split('/')[2]);
                    foreach (var pair in item)
                        if (!(IgnorePrincipalHomepage && path.StartsWith("/servicePrincipals/") && pair.Key == "homepage"))
                            target[pair.Key] = pair.Value?.DeepClone();
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                if (path.EndsWith("/appRoleAssignedTo"))
                {
                    item["principalType"] = "User"; Assignments.Add(item);
                    return Json(item, HttpStatusCode.Created);
                }
                item["id"] = Id(_next++);
                if (path == "/applications") { item["appId"] = Id(_next++); item["passwordCredentials"] = new JsonArray(); item["keyCredentials"] = new JsonArray(); item["appRoles"] = new JsonArray(); Applications.Add(item); }
                else if (path == "/servicePrincipals")
                {
                    var app = Applications.Single(a => Value(a, "appId") == Value(item, "appId"));
                    item["displayName"] = Value(app, "displayName"); item["appOwnerOrganizationId"] = Tenant; item["accountEnabled"] = true;
                    Principals.Add(item);
                }
                else throw new InvalidOperationException("Unexpected write route " + path);
                return Json(item, HttpStatusCode.Created);
            }
            if (path == "/organization") return Page(new[] { new JsonObject { ["id"] = WrongTenant ? Operator : Tenant, ["displayName"] = "Synthetic tenant" } });
            if (path == "/me") return Json(new JsonObject { ["id"] = WrongOperator ? Tenant : Operator });
            if (path.EndsWith("/appRoleAssignments", StringComparison.Ordinal)) return Page(Array.Empty<JsonObject>());
            if (path.EndsWith("/appRoleAssignedTo", StringComparison.Ordinal)) return Page(Assignments.Where(a => a["resourceId"] is null || Value(a, "resourceId") == path.Split('/')[2]));
            if (path == "/oauth2PermissionGrants")
            {
                var client = FilterValue(query);
                return Page(Grants.Where(g => Value(g, "clientId") == client));
            }
            if (path == "/servicePrincipals" && query.Contains(ApplicationSetupService.GraphApplicationId, StringComparison.Ordinal)) return Page(new[] { Resource });
            if (path.StartsWith("/applications/", StringComparison.Ordinal)) return Json(Applications.Single(a => Value(a, "id") == path.Split('/')[2]));
            if (path.StartsWith("/servicePrincipals/", StringComparison.Ordinal)) return Json(Principals.Single(a => Value(a, "id") == path.Split('/')[2]));
            var list = path == "/applications" ? Applications : path == "/servicePrincipals" ? Principals : throw new InvalidOperationException("Unexpected read route " + path);
            var field = query.Contains("appId eq", StringComparison.Ordinal) ? "appId" : "displayName";
            return Page(list.Where(a => Value(a, field) == FilterValue(query)));
        }
        private static string FilterValue(string query) => Regex.Match(query, @"\$filter=(?:appId|displayName|clientId) eq '([^']*)'").Groups[1].Value;
        private static string Value(JsonObject o, string p) => o[p]?.GetValue<string>() ?? "";
        private static HttpResponseMessage Page(IEnumerable<JsonObject> rows) => Json(new JsonObject { ["value"] = new JsonArray(rows.Select(r => r.DeepClone()).ToArray()) });
        private static HttpResponseMessage Json(JsonObject o, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(o.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task Preview_is_read_only_and_resolves_two_distinct_delegated_permission_sets()
    {
        using var f = new Fixture();
        var plan = await f.Preview();
        Assert.Empty(f.Handler.Writes);
        Assert.Equal(2, plan.Rows.Count);
        var assessment = plan.Rows.Single(r => r.Mode == SessionMode.Assessment);
        var deployment = plan.Rows.Single(r => r.Mode == SessionMode.Deployment);
        Assert.DoesNotContain(assessment.Permissions, p => p.Name.Contains("ReadWrite", StringComparison.Ordinal));
        Assert.Contains(deployment.Permissions, p => p.Name == "Policy.ReadWrite.ConditionalAccess");
        Assert.All(plan.Rows, r => Assert.Equal("AzureADMyOrg", r.ApplicationPayload["signInAudience"]!.GetValue<string>()));
        Assert.DoesNotContain("passwordCredentials", ToolkitJson.Serialize(plan), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_dynamic_permission_blocks_preview()
    {
        using var f = new Fixture();
        f.Handler.Resource["oauth2PermissionScopes"]!.AsArray().Clear();
        await Assert.ThrowsAsync<ConfigurationException>(f.Preview);
        Assert.Empty(f.Handler.Writes);
    }

    [Fact]
    public async Task Name_match_is_not_adopted_or_overwritten()
    {
        using var f = new Fixture();
        f.Handler.Applications.Add(new JsonObject { ["id"] = Operator, ["appId"] = Tenant, ["displayName"] = "Test Assessment Tool" });
        var plan = await f.Preview();
        Assert.Equal("Review existing", plan.Rows[0].Status);
        var result = await f.Execute(plan);
        Assert.Equal("Review existing", result.Rows[0].Status);
        Assert.Equal(5, f.Handler.Writes.Count); // Only the missing deployment pair, redirects, icon and enterprise-app properties.
        Assert.Equal(2, f.Handler.Applications.Count);
    }

    [Fact]
    public async Task Wrong_tenant_or_operator_blocks_setup_before_writes()
    {
        using var f = new Fixture();
        f.Handler.WrongTenant = true;
        await Assert.ThrowsAsync<TenantMismatchException>(f.Preview);
        f.Handler.WrongTenant = false; f.Handler.WrongOperator = true;
        await Assert.ThrowsAsync<AuthenticationRequiredException>(f.Preview);
        Assert.Empty(f.Handler.Writes);
    }

    [Fact]
    public async Task Confirmation_tampering_and_standard_changes_block_before_writes()
    {
        using var f = new Fixture();
        var plan = await f.Preview();
        await Assert.ThrowsAsync<PlanValidationException>(() => f.Service.ExecuteAsync(plan, f.Standard, Tenant, false, f.Evidence, CancellationToken.None));
        await Assert.ThrowsAsync<PlanValidationException>(() => f.Service.ExecuteAsync(plan, f.Standard, Operator, true, f.Evidence, CancellationToken.None));
        plan.Rows[0].ApplicationPayload["signInAudience"] = "AzureADMultipleOrgs";
        await Assert.ThrowsAsync<PlanValidationException>(() => f.Execute(plan));
        plan = await f.Preview();
        f.Standard.Release = "changed";
        await Assert.ThrowsAsync<PlanValidationException>(() => f.Execute(plan));
        Assert.Empty(f.Handler.Writes);
    }

    [Fact]
    public async Task Writes_require_durable_before_evidence_and_have_separate_readback_results()
    {
        using var f = new Fixture();
        var plan = await f.Preview();
        f.Handler.OnWrite = _ =>
        {
            var directory = Assert.Single(Directory.GetDirectories(f.Evidence));
            Assert.True(File.Exists(Path.Combine(directory, "before.json")));
            Assert.True(File.Exists(Path.Combine(directory, "result.json")));
            Assert.True(new FileInfo(Path.Combine(directory, "journal.ndjson")).Length > 0);
        };
        var result = await f.Execute(plan);
        Assert.Equal(10, f.Handler.Writes.Count);
        Assert.All(result.Rows, r => { Assert.Equal("Accepted", r.ApplicationWrite); Assert.Equal("Accepted", r.ServicePrincipalWrite); Assert.Equal("Passed", r.ConfigurationVerification); });
        Assert.True(result.AfterComplete);
        Assert.True(File.Exists(Path.Combine(result.EvidenceDirectory, "after.json")));
        await Assert.ThrowsAsync<PlanValidationException>(() => f.Execute(plan));
        Assert.Equal(10, f.Handler.Writes.Count);
    }

    [Fact]
    public async Task Unwritable_evidence_blocks_all_writes()
    {
        using var f = new Fixture();
        var plan = await f.Preview();
        Directory.CreateDirectory(f.Evidence);
        var file = Path.Combine(f.Evidence, "not-a-directory"); File.WriteAllText(file, "synthetic");
        await Assert.ThrowsAnyAsync<IOException>(() => f.Service.ExecuteAsync(plan, f.Standard, Tenant, true, file, CancellationToken.None));
        Assert.Empty(f.Handler.Writes);
    }

    [Fact]
    public async Task Ambiguous_write_is_not_retried_and_blocks_a_fresh_creation_attempt()
    {
        using var f = new Fixture();
        var plan = await f.Preview();
        f.Handler.FailFirstWriteAmbiguously = true;
        var result = await f.Execute(plan);
        Assert.Single(f.Handler.Writes);
        Assert.Equal("Unknown", result.Rows[0].ApplicationWrite);
        Assert.Equal("Not run", result.Rows[1].Status);
        Assert.NotNull(result.EndedAt);
        Assert.True(result.AfterComplete);
        f.Handler.FailFirstWriteAmbiguously = false;
        var nextPlan = await f.Preview();
        await Assert.ThrowsAsync<PlanValidationException>(() => f.Execute(nextPlan));
        Assert.Single(f.Handler.Writes);
    }

    [Fact]
    public async Task Http_408_write_is_ambiguous_and_cannot_be_replayed_from_a_new_preview()
    {
        using var f = new Fixture();
        var plan = await f.Preview();
        f.Handler.TimeoutStatusOnWrite = true;
        var result = await f.Execute(plan);
        Assert.Single(f.Handler.Writes);
        Assert.Equal("Unknown", result.Rows[0].ApplicationWrite);
        f.Handler.TimeoutStatusOnWrite = false;
        var next = await f.Preview();
        await Assert.ThrowsAsync<PlanValidationException>(() => f.Execute(next));
        Assert.Single(f.Handler.Writes);
    }

    [Fact]
    public async Task Stop_finishes_current_application_pair_and_after_evidence_but_not_next_application()
    {
        using var f = new Fixture();
        using var stop = new CancellationTokenSource();
        var plan = await f.Preview();
        f.Handler.OnWrite = token => { stop.Cancel(); Assert.False(token.IsCancellationRequested); };
        var result = await f.Execute(plan, stop.Token);
        Assert.Equal(5, f.Handler.Writes.Count);
        Assert.Equal("Passed", result.Rows[0].ConfigurationVerification);
        Assert.Equal("Not run", result.Rows[1].Status);
        Assert.StartsWith("Stopped", result.Status, StringComparison.Ordinal);
        Assert.True(result.AfterComplete);
    }

    [Fact]
    public async Task Configured_permissions_do_not_claim_consent_or_engineer_assignment()
    {
        using var f = new Fixture();
        var created = await f.Execute(await f.Preview());
        var validation = await f.Service.ValidateAsync(f.Standard, created.Rows[0].ClientId, created.Rows[1].ClientId, CancellationToken.None);
        Assert.False(validation.Ready);
        Assert.All(validation.Rows, r => { Assert.True(r.ConfigurationValid); Assert.False(r.ConsentComplete); Assert.False(r.EngineerAssignmentConfirmed); Assert.Contains("Not tested", r.AccessStatus, StringComparison.Ordinal); });
    }

    [Fact]
    public async Task Validation_checks_actual_grants_and_blocks_extra_assessment_write_access()
    {
        using var f = new Fixture();
        var created = await f.Execute(await f.Preview());
        f.Handler.Assignments.Add(new JsonObject { ["principalId"] = Operator, ["principalType"] = "User" });
        foreach (var row in created.Rows)
            f.Handler.Grants.Add(new JsonObject { ["clientId"] = row.ServicePrincipalId, ["resourceId"] = GraphSp, ["consentType"] = "AllPrincipals", ["scope"] = string.Join(' ', ApplicationSetupService.RequiredScopes(f.Standard, row.Mode)) });
        var validation = await f.Service.ValidateAsync(f.Standard, created.Rows[0].ClientId, created.Rows[1].ClientId, CancellationToken.None);
        Assert.True(validation.Ready);
        f.Handler.Grants[0]["scope"] = f.Handler.Grants[0]["scope"]!.GetValue<string>() + " Application.ReadWrite.All";
        validation = await f.Service.ValidateAsync(f.Standard, created.Rows[0].ClientId, created.Rows[1].ClientId, CancellationToken.None);
        Assert.False(validation.Ready);
        Assert.False(validation.Rows[0].ConfigurationValid);
        Assert.Contains(validation.Rows[0].Issues, i => i.Contains("Extra delegated permissions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Explicit_existing_ids_repair_redirects_branding_and_assignment_without_creating_apps()
    {
        using var f = new Fixture();
        var original = await f.Execute(await f.Preview());
        f.Handler.Writes.Clear();
        f.Handler.Applications[0]["web"] = new JsonObject();
        f.Handler.Applications[0]["displayName"] = "Legacy dedicated tool";
        await Assert.ThrowsAsync<ConfigurationException>(() => f.Service.ValidateConsentRedirectAsync(original.Rows[0].ClientId, default));
        var plan = await f.Service.PreviewAsync(f.Standard, "M365 BuildStandard", default, original.Rows[0].ClientId, original.Rows[1].ClientId, true);
        Assert.All(plan.Rows, r => Assert.Equal("Configure existing", r.Status));
        var result = await f.Execute(plan);
        Assert.Equal(2, f.Handler.Applications.Count);
        Assert.All(result.Rows, r => Assert.Equal("Passed", r.ConfigurationVerification));
        Assert.All(result.Rows, r => Assert.Equal("Not attempted", r.ApplicationWrite));
        Assert.All(result.Rows, r => Assert.All(r.AdditionalWrites, a => Assert.Equal("Accepted", a.Acceptance)));
        Assert.Equal(2, f.Handler.Assignments.Count);
        Assert.DoesNotContain(f.Handler.Writes, p => p is "/applications" or "/servicePrincipals");
        await f.Service.ValidateConsentRedirectAsync(original.Rows[0].ClientId, default);
        Assert.Empty(f.Handler.Grants); // Engineer assignment is not admin consent.
    }

    [Fact]
    public async Task Existing_registration_drift_and_credentials_block_repair_before_any_write()
    {
        using var f = new Fixture();
        var created = await f.Execute(await f.Preview());
        f.Handler.Writes.Clear();
        Task<ApplicationSetupPlan> Preview() => f.Service.PreviewAsync(f.Standard, "Test", default, created.Rows[0].ClientId, created.Rows[1].ClientId);
        var plan = await Preview();
        f.Handler.Applications[0]["displayName"] = "Changed after preview";
        await Assert.ThrowsAsync<PlanValidationException>(() => f.Execute(plan));
        f.Handler.Applications[0]["passwordCredentials"] = new JsonArray(new JsonObject { ["keyId"] = Operator });
        await Assert.ThrowsAsync<ConfigurationException>(Preview);
        Assert.Empty(f.Handler.Writes);
    }

    [Fact]
    public async Task Ambiguous_followup_write_is_journalled_and_blocks_repair_replay()
    {
        using var f = new Fixture();
        f.Handler.FailWriteNumber = 4; // Logo PUT response lost, after app and SP IDs were saved.
        var result = await f.Execute(await f.Preview());
        Assert.Equal("Unknown", result.Rows[0].AdditionalWrites.Last().Acceptance);
        Assert.Equal(4, f.Handler.Writes.Count);
        f.Handler.FailWriteNumber = 0;
        var plan = await f.Service.PreviewAsync(f.Standard, "Test", default, result.Rows[0].ClientId);
        await Assert.ThrowsAsync<PlanValidationException>(() => f.Execute(plan));
        Assert.Equal(4, f.Handler.Writes.Count);
    }

    [Fact]
    public void Setup_callback_and_native_redirects_are_separate_and_exact()
    {
        Assert.Equal("http://localhost:8400/m365-consent/", SetupRegistration.Web()["redirectUris"]![0]!.GetValue<string>());
        Assert.Contains(Operator, SetupRegistration.BrokerRedirect(Operator));
        Assert.Equal(2, SetupRegistration.PublicClient(Operator)["redirectUris"]!.AsArray().Count);
        Assert.Single(SetupRegistration.PublicClient()["redirectUris"]!.AsArray());
    }

    [Fact]
    public async Task Pre_send_setup_auth_failure_is_not_an_unknown_request()
    {
        using var f = new Fixture();
        var plan = await f.Preview();
        f.Handler.OnWrite = _ => f.TokenProvider.Fail = true;
        var result = await f.Execute(plan);
        Assert.Single(f.Handler.Writes);
        Assert.Equal("Accepted", result.Rows[0].ApplicationWrite);
        Assert.Equal("Not attempted", result.Rows[0].ServicePrincipalWrite);
        Assert.Equal("Review required", result.Status);
    }

    [Theory]
    [InlineData("web")]
    [InlineData("credentials")]
    [InlineData("roles")]
    public async Task Setup_transport_rejects_unreviewed_redirects_credentials_and_application_permissions(string mutation)
    {
        using var f = new Fixture(); var plan = await f.Preview();
        var payload = (JsonObject)plan.Rows[0].ApplicationPayload.DeepClone();
        if (mutation == "web") payload["web"]!["redirectUris"] = new JsonArray("https://example.invalid/collect");
        if (mutation == "credentials") payload["passwordCredentials"] = new JsonArray();
        if (mutation == "roles") payload["requiredResourceAccess"]![0]!["resourceAccess"]![0]!["type"] = "Role";
        var graph = new ApplicationSetupGraphClient(new HttpClient(f.Handler), f.TokenProvider, NullLog.Instance);
        await Assert.ThrowsAsync<WriteDeniedException>(() => graph.CreateAsync("/applications", payload, default));
        Assert.Empty(f.Handler.Writes);
    }

    [Fact]
    public async Task Accepted_existing_app_update_with_unconfirmed_homepage_is_reported_as_partial()
    {
        using var f = new Fixture(); var created = await f.Execute(await f.Preview());
        f.Handler.Principals[0]["homepage"] = "https://example.invalid/old";
        f.Handler.IgnorePrincipalHomepage = true;
        var plan = await f.Service.PreviewAsync(f.Standard, "Test", default, created.Rows[0].ClientId, created.Rows[1].ClientId);
        var result = await f.Execute(plan);
        Assert.Equal("Partially completed — review", result.Rows[0].Status);
        Assert.Equal("Incomplete", result.Rows[0].ConfigurationVerification);
        Assert.All(result.Rows[0].AdditionalWrites, w => Assert.Equal("Accepted", w.Acceptance));
        Assert.Equal("Not run", result.Rows[1].Status);
    }

    [Fact]
    public void Consent_link_is_tenant_bound_and_has_no_secret()
    {
        var uri = ApplicationSetupService.AdminConsentUri(Tenant, Operator, new[] { "User.Read" });
        Assert.Equal("login.microsoftonline.com", uri.Host);
        Assert.Contains(Tenant, uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("scope=https%3A%2F%2Fgraph.microsoft.com%2FUser.Read", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("client_secret", uri.Query, StringComparison.Ordinal);
        Assert.Throws<ConfigurationException>(() => ApplicationSetupService.AdminConsentUri("common", Operator));
    }
}
