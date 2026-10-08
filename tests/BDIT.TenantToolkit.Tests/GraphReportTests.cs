using System.Text.Json.Nodes;
using System.IO.Compression;
using System.Runtime.CompilerServices;
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

    [Fact]
    public async Task Duplicate_subscription_identities_become_a_partial_section_not_a_refused_report()
    {
        var graph = new Reads();
        var sku = ToolkitJson.ParseObject($$"""{"id":"pool_{{TestData.ClientId}}","skuId":"{{TestData.ClientId}}","skuPartNumber":"SYNTHETIC_PRODUCT","consumedUnits":1,"prepaidUnits":{"enabled":1,"warning":0,"suspended":0} }""");
        graph.Responses["/subscribedSkus"] = [sku, (JsonObject)sku.DeepClone()];
        var report = await Service().CaptureAsync(graph, Session(), "users-licences", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.Partial, report.Sections[0].Status);
        var row = Assert.Single(report.Sections[0].Rows);
        Assert.Equal(ReportReadState.Partial, row["readStatus"]!.GetValue<string>()); Assert.Contains("duplicated", row["error"]!.GetValue<string>());
        Assert.Equal(report.Id, ReportEvidenceSchema.Read(ReportEvidenceSchema.Serialize(report), TestData.TenantA).Id);
    }

    [Theory]
    [InlineData("sign-ins", "")]
    [InlineData("sign-ins", " synthetic-event")]
    [InlineData("directory-audit", " ")]
    public async Task Empty_or_blank_log_identities_count_as_missing_and_stay_partial(string reportId, string id)
    {
        var graph = new Reads();
        var item = ToolkitJson.ParseObject("""{"createdDateTime":"2026-09-10T01:00:00Z","activityDateTime":"2026-09-10T01:00:00Z","result":"success","targetResources":[],"status":{"errorCode":0}}"""); item["id"] = id;
        graph.Responses[reportId == "sign-ins" ? "/auditLogs/signIns" : "/auditLogs/directoryAudits"] = [item];
        var report = await Service().CaptureAsync(graph, Session(), reportId, Dates(), CancellationToken.None);
        Assert.Equal(ReportReadState.Partial, report.Status);
        var row = Assert.Single(report.Sections[0].Rows);
        Assert.Null(row["id"]); Assert.Contains("identity", row["error"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("servicePlanName")]
    [InlineData("provisioningStatus")]
    [InlineData("skuPartNumber")]
    public async Task Empty_required_licence_text_is_partial_evidence_not_a_refused_report(string field)
    {
        var graph = new Reads(); graph.Responses["/users"] = [User(TestData.Operator)];
        var details = ToolkitJson.ParseObject($$"""{"skuId":"{{TestData.ClientId}}","skuPartNumber":"SYNTHETIC_PRODUCT","servicePlans":[{"servicePlanId":"efb87545-963c-4e0d-99df-69c6916d9eb0","servicePlanName":"SYNTHETIC_PLAN","provisioningStatus":"Success","appliesTo":"User"}] }""");
        if (field == "skuPartNumber") details[field] = ""; else details["servicePlans"]![0]![field] = "";
        graph.Responses["/users/" + TestData.Operator + "/licenseDetails"] = [details];
        var report = await Service().CaptureAsync(graph, Session(), "users-licences", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.Partial, report.Sections[1].Status);
        Assert.Equal(ReportReadState.Partial, report.Sections[1].Rows[0]["productsReadStatus"]!.GetValue<string>());
    }

    [Fact]
    public async Task Zero_guid_user_identity_is_partial_without_a_licence_query()
    {
        var graph = new Reads(); graph.Responses["/users"] = [User(Guid.Empty.ToString())];
        var report = await Service().CaptureAsync(graph, Session(), "users-licences", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.Partial, report.Sections[1].Status); Assert.Null(report.Sections[1].Rows[0]["id"]);
        Assert.DoesNotContain(graph.Paths, p => p.Contains("/licenseDetails", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Whitespace_padded_user_identity_is_malformed_not_a_second_licensed_user()
    {
        var graph = new Reads(); graph.Responses["/users"] = [User(TestData.Operator), User(TestData.Operator + "\n")];
        var report = await Service().CaptureAsync(graph, Session(), "users-licences", new(), CancellationToken.None);
        var rows = report.Sections[1].Rows;
        Assert.Equal(ReportReadState.Partial, report.Sections[1].Status);
        Assert.Single(rows, r => r["id"]?.GetValue<string>() == TestData.Operator);
        Assert.Single(rows, r => r["id"] is null && r["readStatus"]!.GetValue<string>() == ReportReadState.Partial);
        Assert.Single(graph.Paths, p => p.Contains("/licenseDetails", StringComparison.Ordinal));
    }

    [Fact]
    public async Task User_identities_are_normalised_before_duplicates_are_detected()
    {
        const string id = "efb87545-963c-4e0d-99df-69c6916d9eb0";
        var graph = new Reads(); graph.Responses["/users"] = [User(id), User(id.ToUpperInvariant())];
        var report = await Service().CaptureAsync(graph, Session(), "users-licences", new(), CancellationToken.None);
        var row = Assert.Single(report.Sections[1].Rows);
        Assert.Equal(id, row["id"]!.GetValue<string>()); Assert.Equal(ReportReadState.Partial, row["readStatus"]!.GetValue<string>());
        Assert.DoesNotContain(graph.Paths, p => p.Contains("/licenseDetails", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("blank-text-id")]
    [InlineData("padded-text-id")]
    [InlineData("zero-guid")]
    [InlineData("non-canonical-guid")]
    [InlineData("empty-service-plan-name")]
    [InlineData("empty-sku-part-number")]
    [InlineData("subscription-without-sku")]
    public async Task Strict_reader_still_refuses_malformed_identity_or_empty_text_presented_as_collected(string defect)
    {
        var graph = new Reads();
        graph.Responses["/subscribedSkus"] = [ToolkitJson.ParseObject($$"""{"id":"pool_{{TestData.ClientId}}","skuId":"{{TestData.ClientId}}","skuPartNumber":"SYNTHETIC_PRODUCT","capabilityStatus":"Enabled","consumedUnits":1,"prepaidUnits":{"enabled":1,"warning":0,"suspended":0} }""")];
        graph.Responses["/users"] = [User(TestData.Operator)];
        graph.Responses["/users/" + TestData.Operator + "/licenseDetails"] = [ToolkitJson.ParseObject($$"""{"skuId":"{{TestData.ClientId}}","skuPartNumber":"SYNTHETIC_PRODUCT","servicePlans":[{"servicePlanId":"{{TestData.Office}}","servicePlanName":"SYNTHETIC_PLAN","provisioningStatus":"Success","appliesTo":"User"}]}""")];
        var report = await Service().CaptureAsync(graph, Session(), "users-licences", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.Collected, report.Status);
        var node = JsonNode.Parse(ReportEvidenceSchema.Serialize(report))!.AsObject();
        var subscription = node["sections"]![0]!["rows"]![0]!.AsObject(); var user = node["sections"]![1]!["rows"]![0]!.AsObject();
        switch (defect)
        {
            case "blank-text-id": subscription["id"] = " "; break;
            case "padded-text-id": subscription["id"] = "pool_synthetic\n"; break;
            case "zero-guid": user["id"] = Guid.Empty.ToString(); break;
            case "non-canonical-guid": user["id"] = "EFB87545-963C-4E0D-99DF-69C6916D9EB0"; break;
            case "empty-service-plan-name": user["products"]![0]!["servicePlans"]![0]!["servicePlanName"] = ""; break;
            case "empty-sku-part-number": user["products"]![0]!["skuPartNumber"] = ""; break;
            case "subscription-without-sku": subscription["skuId"] = null; break;
        }
        node["integrityDigest"] = EvidenceIntegrity.Compute(node);
        Assert.Throws<ConfigurationException>(() => ReportEvidenceSchema.Read(node.ToJsonString(), TestData.TenantA));
    }

    [Fact]
    public async Task Row_cap_stops_the_read_and_keeps_capped_rows_as_partial_with_a_truncation_note()
    {
        var graph = new PagedReads { Rows = 60_000 };
        var report = await Service().CaptureAsync(graph, Session(), "intune-devices", new(), CancellationToken.None);
        Assert.Equal(GraphReportRegistry.MaximumRows + 1, graph.Yielded);
        Assert.Equal(GraphReportRegistry.MaximumRows, report.Sections[0].Rows.Count);
        Assert.All(report.Sections[0].Rows, r => Assert.Equal(ReportReadState.Collected, r["readStatus"]!.GetValue<string>()));
        Assert.Equal(ReportReadState.Partial, report.Status); Assert.Contains("row cap", report.Sections[0].Error);
        Assert.Equal(report.Id, ReportEvidenceSchema.Read(ReportEvidenceSchema.Serialize(report), TestData.TenantA).Id);
        var exact = await Service().CaptureAsync(new PagedReads { Rows = GraphReportRegistry.MaximumRows }, Session(), "intune-devices", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.Collected, exact.Status); Assert.Equal(GraphReportRegistry.MaximumRows, exact.Sections[0].Rows.Count);
    }

    [Fact]
    public async Task Read_timeout_mid_read_keeps_returned_rows_and_says_so()
    {
        var graph = new PagedReads { Rows = 3, ThenWaitForCancellation = true };
        var report = await new GraphReportService(new FixedClock(), TimeSpan.FromMilliseconds(50)).CaptureAsync(graph, Session(), "intune-devices", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.Cancelled, report.Status); Assert.Equal(3, report.Sections[0].Rows.Count);
        Assert.Contains("after 3 returned rows", report.Sections[0].Error);
        Assert.Equal(report.Id, ReportEvidenceSchema.Read(ReportEvidenceSchema.Serialize(report), TestData.TenantA).Id);
    }

    [Fact]
    public async Task Cancellation_before_any_row_never_claims_retained_rows()
    {
        var report = await new GraphReportService(new FixedClock(), TimeSpan.FromMilliseconds(20)).CaptureAsync(new PagedReads { ThenWaitForCancellation = true }, Session(), "intune-devices", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.Cancelled, report.Status); Assert.Empty(report.Sections[0].Rows);
        Assert.Contains("no rows are retained", report.Sections[0].Error); Assert.DoesNotContain("retained rows are partial", report.Sections[0].Error);
    }

    [Fact]
    public async Task Failed_later_page_keeps_returned_rows_as_partial_not_failed()
    {
        var graph = new PagedReads { Rows = 2, ThenThrow = new GraphRequestException(403, "GET", "/synthetic", "denied", "Synthetic denied page") };
        var report = await Service().CaptureAsync(graph, Session(), "intune-devices", new(), CancellationToken.None);
        Assert.Equal(ReportReadState.Partial, report.Status); Assert.Equal(2, report.Sections[0].Rows.Count);
        Assert.Contains("after 2 returned rows", report.Sections[0].Error);
    }

    [Theory]
    [InlineData("sign-ins", "/auditLogs/signIns", "createdDateTime")]
    [InlineData("directory-audit", "/auditLogs/directoryAudits", "activityDateTime")]
    public async Task Log_queries_use_documented_inclusive_operators_without_select(string reportId, string route, string field)
    {
        var graph = new Reads();
        await Service().CaptureAsync(graph, Session(), reportId, Dates(), CancellationToken.None);
        var path = Assert.Single(graph.Paths);
        Assert.Equal(route + "?$filter=" + field + "%20ge%202026-09-10T00%3A00%3A00Z%20and%20" + field + "%20le%202026-09-10T23%3A59%3A59.9999999Z", path);
        Assert.DoesNotContain("%20lt%20", path); Assert.DoesNotContain("$select", path);
        graph.Responses[route] = [ToolkitJson.ParseObject("""{"id":"synthetic-event","createdDateTime":"2026-09-11T00:00:00Z","activityDateTime":"2026-09-11T00:00:00Z","result":"success","targetResources":[],"status":{"errorCode":0}}""")];
        var boundary = await Service().CaptureAsync(graph, Session(), reportId, Dates(), CancellationToken.None);
        Assert.Contains("outside the requested interval", boundary.Sections[0].Rows[0]["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Other_reports_keep_their_select_projection()
    {
        var graph = new Reads();
        await Service().CaptureAsync(graph, Session(), "intune-devices", new(), CancellationToken.None);
        await Service().CaptureAsync(graph, Session(), "mfa-registration", new(), CancellationToken.None);
        Assert.All(graph.Paths, p => Assert.Contains("?$select=id,", p));
    }

    [Fact]
    public async Task Exports_neutralise_every_formula_prefix_and_write_no_spreadsheet_formulas()
    {
        var graph = new Reads();
        graph.Responses["/deviceManagement/managedDevices"] = [new JsonObject { ["id"] = TestData.Operator, ["deviceName"] = "=HYPERLINK(\"x\")", ["operatingSystem"] = "\t=1+1", ["managedDeviceOwnerType"] = "@SUM(1)", ["complianceState"] = "<script>alert(1)</script>", ["lastSyncDateTime"] = "2026-09-10T00:00:00Z", ["osVersion"] = "-1+2", ["userId"] = "+1" }];
        var report = await Service().CaptureAsync(graph, Session(), "intune-devices", new(), CancellationToken.None);
        var csv = CsvWriter.Write(RegisteredReportDocuments.Sheets(report)[1].Rows);
        Assert.DoesNotContain(",\"=", csv); Assert.DoesNotContain(",\"\t", csv); Assert.DoesNotContain(",\"@", csv); Assert.DoesNotContain(",\"-", csv);
        var html = RegisteredReportDocuments.Html(report, "<b>company</b>");
        Assert.DoesNotContain("<script>", html); Assert.DoesNotContain("<b>company", html);
        using var zip = new ZipArchive(new MemoryStream(XlsxWriter.Write(RegisteredReportDocuments.Sheets(report))));
        foreach (var entry in zip.Entries) { using var reader = new StreamReader(entry.Open()); Assert.DoesNotContain("<f>", reader.ReadToEnd()); }
    }

    private static JsonObject User(string? id) => new() { ["id"] = id, ["displayName"] = "Same synthetic name", ["userPrincipalName"] = "user@example.invalid", ["accountEnabled"] = true, ["userType"] = "Member", ["assignedLicenses"] = new JsonArray(), ["assignedPlans"] = new JsonArray() };
    /// <summary>Streams synthetic complete device rows through the bounded read, then optionally fails or waits for cancellation.</summary>
    private sealed class PagedReads : IGraphClient
    {
        public string TenantId => TestData.TenantA;
        public SessionMode Mode => SessionMode.Deployment;
        public int Rows { get; init; }
        public bool ThenWaitForCancellation { get; init; }
        public Exception? ThenThrow { get; init; }
        public int Yielded { get; private set; }
        public async IAsyncEnumerable<JsonObject> GetBoundedAsync(GraphApi api, string path, int maxItems, [EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 0; i < Rows && Yielded < maxItems; i++)
            {
                ct.ThrowIfCancellationRequested(); Yielded++;
                yield return new JsonObject { ["id"] = new Guid(i + 1, 0, 0, new byte[8]).ToString(), ["deviceName"] = "Synthetic device", ["managedDeviceOwnerType"] = "company", ["operatingSystem"] = "Windows", ["complianceState"] = "compliant", ["lastSyncDateTime"] = "2026-09-10T00:00:00Z" };
            }
            if (ThenThrow is not null) throw ThenThrow;
            if (ThenWaitForCancellation) await Task.Delay(Timeout.Infinite, ct);
        }
        public Task<IReadOnlyList<JsonObject>> GetAllAsync(GraphApi api, string path, CancellationToken ct) => throw new InvalidOperationException("Reports must use the bounded read.");
        public Task<JsonObject> GetAsync(GraphApi api, string path, CancellationToken ct) => throw new InvalidOperationException("No singleton report requests expected.");
        public Task<JsonObject> WriteAsync(GraphApi api, GraphWriteMethod method, string path, JsonObject payload, CancellationToken ct) => throw new InvalidOperationException("Reports cannot write.");
    }

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
