using System.Text.Json.Nodes;
using System.IO.Compression;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Reports;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class GraphReportTests
{
    private static TenantSession Session() { var session = TestData.Session(); session.Scopes = ["Organization.Read.All", "User.Read.All", "DeviceManagementManagedDevices.Read.All", "AuditLog.Read.All"]; return session; }
    private static GraphReportService Service() => new(new FixedClock());
    private static ReportParameters Dates() => new() { Start = "2026-09-10T00:00:00Z", End = "2026-09-11T00:00:00Z" };

    [Theory]
    [InlineData("intune-devices")]
    [InlineData("mfa-registration")]
    [InlineData("sign-ins")]
    [InlineData("directory-audit")]
    [InlineData("users-licences")]
    public async Task Genuine_empty_reads_are_collected_but_failed_reads_are_never_empty_success(string id)
    {
        var graph = new Reads();
        var definition = GraphReportRegistry.Find(id);
        var parameters = definition.DateRange ? Dates() : new ReportParameters();
        var empty = await Service().CaptureAsync(graph, Session(), id, parameters, CancellationToken.None);
        Assert.Equal(ReportReadState.Collected, empty.Status); Assert.All(empty.Sections, s => Assert.Empty(s.Rows));
        var json = ReportEvidenceSchema.Serialize(empty);
        Assert.Equal(empty.Id, ReportEvidenceSchema.Read(json, TestData.TenantA).Id);
        graph.Failure = new GraphRequestException(403, "GET", "/synthetic", "denied", "Synthetic denied read");
        var failed = await Service().CaptureAsync(graph, Session(), id, parameters, CancellationToken.None);
        Assert.Equal(ReportReadState.Failed, failed.Status);
        Assert.All(failed.Sections, s => { Assert.Empty(s.Rows); Assert.NotNull(s.Error); });
        Assert.Equal(0, graph.Writes);
    }

    [Fact]
    public async Task User_products_and_service_plans_are_observed_per_identity_without_name_joins()
    {
        var graph = new Reads();
        graph.Responses["/subscribedSkus"] = [ToolkitJson.ParseObject($$"""{"id":"pool_{{TestData.ClientId}}","skuId":"{{TestData.ClientId}}","skuPartNumber":"SYNTHETIC_PRODUCT","consumedUnits":2,"prepaidUnits":{"enabled":3,"warning":0,"suspended":0} }""")];
        graph.Responses["/users"] = [User(TestData.Operator), User(TestData.Mam)];
        graph.Responses["/users/" + TestData.Operator + "/licenseDetails"] = [ToolkitJson.ParseObject($$"""{"skuId":"{{TestData.ClientId}}","skuPartNumber":"SYNTHETIC_PRODUCT","servicePlans":[{"servicePlanId":"{{TestData.Office}}","servicePlanName":"SYNTHETIC_PLAN","provisioningStatus":"Success","appliesTo":"User"}]}""")];
        var report = await Service().CaptureAsync(graph, Session(), "users-licences", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.Collected, report.Status);
        var users = report.Sections.Single(s => s.Id == "users").Rows;
        Assert.Equal(2, users.Count); Assert.Equal("Same synthetic name", users[0]["name"]!.GetValue<string>()); Assert.Equal(users[0]["name"]!.ToString(), users[1]["name"]!.ToString());
        Assert.Single(users[0]["products"]!.AsArray()); Assert.Empty(users[1]["products"]!.AsArray());
        Assert.Equal("Success", users[0]["products"]![0]!["servicePlans"]![0]!["provisioningStatus"]!.GetValue<string>());
        Assert.Contains(graph.Paths, p => p == "/users/" + TestData.Operator + "/licenseDetails");
        Assert.Contains(graph.Paths, p => p == "/users/" + TestData.Mam + "/licenseDetails");
        Assert.Equal("pool_" + TestData.ClientId, report.Sections[0].Rows[0]["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Missing_and_duplicate_user_identity_never_drives_guessed_licence_queries()
    {
        var graph = new Reads(); var user = User(TestData.Operator);
        graph.Responses["/users"] = [user, (JsonObject)user.DeepClone(), User(null)];
        var report = await Service().CaptureAsync(graph, Session(), "users-licences", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.Partial, report.Status); Assert.Equal(2, report.Sections[1].Rows.Count);
        Assert.DoesNotContain(graph.Paths, p => p.Contains("/licenseDetails", StringComparison.Ordinal));
        Assert.All(report.Sections[1].Rows, r => Assert.Equal(ReportReadState.NotAttempted, r["productsReadStatus"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Unreadable_or_malformed_service_plans_cannot_prove_no_entitlement()
    {
        var graph = new Reads(); graph.Responses["/users"] = [User(TestData.Operator)];
        graph.Responses["/users/" + TestData.Operator + "/licenseDetails"] = [ToolkitJson.ParseObject($$"""{"skuId":"{{TestData.ClientId}}","skuPartNumber":"SYNTHETIC_PRODUCT","servicePlans":["invalid shape"]}""")];
        var partial = await Service().CaptureAsync(graph, Session(), "users-licences", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.Partial, partial.Status); Assert.Equal(ReportReadState.Partial, partial.Sections[1].Rows[0]["productsReadStatus"]!.GetValue<string>());
        graph.Before = path => { if (path.Contains("/licenseDetails", StringComparison.Ordinal)) throw new IOException("Synthetic failed detail read"); };
        var failed = await Service().CaptureAsync(graph, Session(), "users-licences", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.Partial, failed.Status); Assert.Equal(ReportReadState.Failed, failed.Sections[1].Rows[0]["productsReadStatus"]!.GetValue<string>());
    }

    [Fact]
    public async Task Missing_access_is_explicit_not_attempted_without_any_request_or_consent()
    {
        var graph = new Reads(); var session = Session(); session.Scopes.Remove("AuditLog.Read.All");
        var report = await Service().CaptureAsync(graph, session, "mfa-registration", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.NotAttempted, report.Status); Assert.Empty(graph.Paths);
        Assert.Contains("AuditLog.Read.All", report.Sections[0].Error); Assert.Contains("consent", report.Sections[0].Error);
    }

    [Fact]
    public async Task Registration_reports_labels_and_booleans_without_contact_values_or_enforcement_claims()
    {
        var graph = new Reads(); graph.Responses["/reports/authenticationMethods/userRegistrationDetails"] = [ToolkitJson.ParseObject($$"""{"id":"{{TestData.Operator}}","userDisplayName":"Synthetic user","userPrincipalName":"user@example.invalid","isMfaRegistered":false,"isMfaCapable":false,"isSsprRegistered":false,"methodsRegistered":["email"],"phoneNumber":"UNNECESSARY_CONTACT_VALUE","recoveryCodes":["UNNECESSARY_CODE"]}""")];
        var report = await Service().CaptureAsync(graph, Session(), "mfa-registration", new(), CancellationToken.None);
        var json = ReportEvidenceSchema.Serialize(report);
        Assert.DoesNotContain("UNNECESSARY", json); Assert.DoesNotContain("phoneNumber", json);
        Assert.False(report.Sections[0].Rows[0]["isMfaRegistered"]!.GetValue<bool>());
        Assert.Contains(report.Limitations, l => l.Contains("does not prove enforcement", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_device_and_signin_values_stay_null_without_zero_success_or_inferred_form_factor()
    {
        var graph = new Reads(); graph.Responses["/deviceManagement/managedDevices"] = [new JsonObject { ["id"] = TestData.Mam, ["deviceName"] = "Synthetic device" }];
        var devices = await Service().CaptureAsync(graph, Session(), "intune-devices", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.Partial, devices.Status); Assert.Null(devices.Sections[0].Rows[0]["ownership"]); Assert.Null(devices.Sections[0].Rows[0]["lastSyncAt"]);
        graph.Responses["/auditLogs/signIns"] = [new JsonObject { ["id"] = "synthetic-event", ["userId"] = TestData.Operator, ["createdDateTime"] = "2026-09-10T12:00:00Z" }];
        var logs = await Service().CaptureAsync(graph, Session(), "sign-ins", Dates(), CancellationToken.None);
        Assert.Equal(ReportReadState.Partial, logs.Status); Assert.Null(logs.Sections[0].Rows[0]["errorCode"]);
        Assert.Contains(graph.Paths, p => p.Contains("$filter=createdDateTime%20ge%202026", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancellation_retains_checked_user_rows_and_context_changes_refuse_acceptance()
    {
        var graph = new Reads(); graph.Responses["/users"] = [User(TestData.Operator), User(TestData.Mam)];
        using var stop = new CancellationTokenSource();
        graph.Before = path => { if (path.Contains(TestData.Mam + "/licenseDetails", StringComparison.Ordinal)) stop.Cancel(); };
        var cancelled = await Service().CaptureAsync(graph, Session(), "users-licences", new(), stop.Token);
        Assert.Equal(ReportReadState.Cancelled, cancelled.Status); Assert.Single(cancelled.Sections[1].Rows);
        Assert.Equal(ReportReadState.Collected, cancelled.Sections[0].Status);
        var session = Session(); graph.Before = _ => session.AccountObjectId = TestData.Mam;
        await Assert.ThrowsAsync<TenantMismatchException>(() => Service().CaptureAsync(graph, session, "intune-devices", new(), CancellationToken.None));
    }

    [Theory]
    [InlineData("unknown-row-field")]
    [InlineData("missing-row-field")]
    [InlineData("wrong-row-type")]
    [InlineData("future-schema")]
    [InlineData("wrong-status")]
    [InlineData("full-capture-claim")]
    public async Task Strict_reader_refuses_unregistered_or_malformed_contracts_even_with_recomputed_hash(string defect)
    {
        var graph = new Reads(); graph.Responses["/deviceManagement/managedDevices"] = [new JsonObject { ["id"] = TestData.Mam }];
        var report = await Service().CaptureAsync(graph, Session(), "intune-devices", new(), CancellationToken.None);
        var node = JsonNode.Parse(ReportEvidenceSchema.Serialize(report))!.AsObject();
        var row = node["sections"]![0]!["rows"]![0]!.AsObject();
        switch (defect)
        {
            case "unknown-row-field": row["credential"] = "refused synthetic value"; break;
            case "missing-row-field": row.Remove("ownership"); break;
            case "wrong-row-type": row["lastSyncAt"] = true; break;
            case "future-schema": node["schemaVersion"] = 2; break;
            case "wrong-status": node["status"] = ReportReadState.Collected; break;
            case "full-capture-claim": node["complete"] = true; break;
        }
        node["integrityDigest"] = EvidenceIntegrity.Compute(node);
        Assert.Throws<ConfigurationException>(() => ReportEvidenceSchema.Read(node.ToJsonString(), TestData.TenantA));
    }

    [Fact]
    public async Task Report_wrappers_cannot_be_loaded_as_ordinary_configuration_and_cross_tenant_reads_refuse()
    {
        var report = await Service().CaptureAsync(new Reads(), Session(), "intune-devices", new(), CancellationToken.None);
        using var root = new TempRoot(); var file = Path.Combine(root.Root, "synthetic-report.json");
        File.WriteAllText(file, ReportEvidenceSchema.Serialize(report));
        Assert.Throws<ConfigurationException>(() => AssessmentContext.ReadPrimary(file));
        Assert.Throws<TenantMismatchException>(() => ReportEvidenceSchema.Read(File.ReadAllText(file), TestData.TenantB));
    }

    [Fact]
    public async Task Immutable_store_keeps_required_nulls_and_refuses_overwrite_or_cross_tenant_rebinding()
    {
        using var root = new TempRoot(); var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var report = await Service().CaptureAsync(new Reads(), Session(), "intune-devices", new(), CancellationToken.None);
        var file = store.SaveReport(report); var original = File.ReadAllBytes(file);
        Assert.Equal(report.IntegrityDigest, store.LoadReport(TestData.TenantA, report.Id)!.IntegrityDigest);
        Assert.Contains("\"start\": null", File.ReadAllText(file));
        Assert.Throws<SafetyViolationException>(() => store.SaveReport(report));
        Assert.Equal(original, File.ReadAllBytes(file)); Assert.Contains("report-evidence", file);
        Assert.Empty(Directory.GetFiles(root.Root, "*.tmp", SearchOption.AllDirectories));
        var other = file.Replace(TestData.TenantA, TestData.TenantB, StringComparison.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(other)!); File.Copy(file, other);
        Assert.Throws<TenantMismatchException>(() => store.LoadReport(TestData.TenantB, report.Id));
        Assert.Null(store.LoadReport(TestData.TenantA, Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task Exports_escape_content_guard_formulas_and_preserve_strict_source_and_empty_headers()
    {
        using var root = new TempRoot(); var graph = new Reads();
        graph.Responses["/deviceManagement/managedDevices"] = [new JsonObject { ["id"] = TestData.Mam, ["deviceName"] = "=HYPERLINK(<script>synthetic</script>)" }];
        var report = await Service().CaptureAsync(graph, Session(), "intune-devices", new(), CancellationToken.None);
        var original = ReportEvidenceSchema.Serialize(report); var exporter = new ReportExporter(root.Paths, "<script>company</script>");
        var html = File.ReadAllText(exporter.ExportReport(report, ExportFormat.Html));
        Assert.DoesNotContain("<script>", html); Assert.Contains("&lt;script&gt;", html); Assert.Contains("default-src 'none'", html);
        using (var zip = ZipFile.OpenRead(exporter.ExportReport(report, ExportFormat.Csv)))
        { using var reader = new StreamReader(zip.Entries.Single(e => e.Name == "02_devices.csv").Open()); Assert.Contains("'=HYPERLINK", reader.ReadToEnd()); }
        Assert.Equal(report.Id, ReportEvidenceSchema.Read(File.ReadAllText(exporter.ExportReport(report, ExportFormat.Json)), TestData.TenantA).Id);
        Assert.NotEmpty(File.ReadAllBytes(exporter.ExportReport(report, ExportFormat.Xlsx)));
        Assert.Equal(original, ReportEvidenceSchema.Serialize(report));
        var empty = await Service().CaptureAsync(new Reads(), Session(), "intune-devices", new(), CancellationToken.None);
        Assert.Contains("Checked successfully; no objects returned.", RegisteredReportDocuments.Html(empty, "BDIT"));
        Assert.Contains("Object ID", RegisteredReportDocuments.Sheets(empty).Single(s => s.Name == "devices").Rows[0]);
        graph.Failure = new IOException("Synthetic inaccessible inventory");
        var failed = await Service().CaptureAsync(graph, Session(), "intune-devices", new(), CancellationToken.None);
        var failedHtml = RegisteredReportDocuments.Html(failed, "BDIT");
        Assert.Contains("not an empty successful check", failedHtml); Assert.DoesNotContain("Checked successfully; no objects returned.", failedHtml);
    }

    [Fact]
    public async Task Read_budget_cancels_without_empty_success_and_parameters_refuse_before_requests()
    {
        var graph = new Reads { WaitForCancellation = true };
        var cancelled = await new GraphReportService(new FixedClock(), TimeSpan.FromMilliseconds(20)).CaptureAsync(graph, Session(), "intune-devices", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.Cancelled, cancelled.Status); Assert.NotNull(cancelled.Sections[0].Error);
        graph.Paths.Clear();
        await Assert.ThrowsAsync<ConfigurationException>(() => Service().CaptureAsync(graph, Session(), "sign-ins", new() { Start = "2026-01-01T00:00:00Z", End = "2026-09-10T00:00:00Z" }, CancellationToken.None));
        await Assert.ThrowsAsync<ConfigurationException>(() => Service().CaptureAsync(graph, Session(), "sign-ins", new() { Start = "2026-09-11T00:00:00Z", End = "2026-09-12T00:00:00Z" }, CancellationToken.None));
        Assert.Empty(graph.Paths);
    }

    [Fact]
    public async Task Existing_directory_permission_is_reused_only_for_documented_routes()
    {
        var graph = new Reads(); var session = Session(); session.Scopes = ["Directory.Read.All"];
        var audit = await Service().CaptureAsync(graph, session, "directory-audit", Dates(), CancellationToken.None);
        Assert.Equal(ReportReadState.Collected, audit.Status); Assert.Single(graph.Paths); graph.Paths.Clear();
        var registration = await Service().CaptureAsync(graph, session, "mfa-registration", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.NotAttempted, registration.Status); Assert.Empty(graph.Paths);
        var signin = await Service().CaptureAsync(graph, session, "sign-ins", Dates(), CancellationToken.None);
        Assert.Equal(ReportReadState.NotAttempted, signin.Status); Assert.Empty(graph.Paths);
    }

    private static JsonObject User(string? id) => new() { ["id"] = id, ["displayName"] = "Same synthetic name", ["userPrincipalName"] = "user@example.invalid", ["accountEnabled"] = true, ["userType"] = "Member", ["assignedLicenses"] = new JsonArray(), ["assignedPlans"] = new JsonArray() };
    private sealed class Reads : IGraphClient
    {
        public string TenantId => TestData.TenantA;
        public SessionMode Mode => SessionMode.Deployment;
        public Dictionary<string, List<JsonObject>> Responses { get; } = new();
        public List<string> Paths { get; } = [];
        public Exception? Failure { get; set; }
        public bool WaitForCancellation { get; set; }
        public Action<string>? Before { get; set; }
        public int Writes { get; private set; }
        public async Task<IReadOnlyList<JsonObject>> GetAllAsync(GraphApi api, string path, CancellationToken ct)
        {
            Paths.Add(path); Before?.Invoke(path); ct.ThrowIfCancellationRequested();
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, ct);
            if (Failure is not null) throw Failure;
            return Responses.GetValueOrDefault(path.Split('?')[0], []).Select(r => (JsonObject)r.DeepClone()).ToList();
        }
        public Task<JsonObject> GetAsync(GraphApi api, string path, CancellationToken ct) => throw new InvalidOperationException("No singleton report requests expected.");
        public Task<JsonObject> WriteAsync(GraphApi api, GraphWriteMethod method, string path, JsonObject payload, CancellationToken ct) { Writes++; throw new InvalidOperationException("Reports cannot write."); }
    }
}
