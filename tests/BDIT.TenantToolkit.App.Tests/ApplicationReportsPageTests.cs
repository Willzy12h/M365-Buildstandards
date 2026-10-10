using System.IO.Compression;
using System.Net;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ApplicationReportsPageTests
{
    private static GraphReportsViewModel Page(ConnectedReportsFixture f, string id = "application-expiry")
    {
        f.Workspace.Session!.Scopes.Add(ApplicationReportRegistry.ApplicationScope);
        var page = f.Page(); page.SelectedReport = GraphReportPresentation.Find(2, id); return page;
    }
    [Theory]
    [InlineData("application-expiry")] [InlineData("application-consent")]
    public async Task Successful_zero_reads_round_trip_without_changing_snapshot_plan_or_acknowledgement(string id)
    {
        using var f = new ConnectedReportsFixture(); f.Workspace.Session!.Scopes.Add(ApplicationReportRegistry.DirectoryScope);
        var snapshot = TestData.Snapshot(f.Workspace.RequireStandard());
        f.Set(nameof(BDIT.TenantToolkit.App.Services.Workspace.Snapshot), snapshot); f.Set(nameof(BDIT.TenantToolkit.App.Services.Workspace.AcknowledgedSnapshotId), snapshot.Id);
        f.Set(nameof(BDIT.TenantToolkit.App.Services.Workspace.Plan), new DeploymentPlan { TenantId = TestData.TenantA, Id = Guid.NewGuid().ToString() });
        var state = ToolkitJson.Serialize(new { f.Workspace.Snapshot, f.Workspace.Plan, f.Workspace.AcknowledgedSnapshotId });
        var page = Page(f, id); page.OnNavigatedTo(); page.RefreshStored(); Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
        Assert.Equal(7, page.Reports.Count); Assert.Equal("30", page.HorizonDays); Assert.False(page.RequiresDates);
        Assert.Equal(id == "application-expiry", page.RequiresHorizon); Assert.False(page.ReadCommand.CanExecute(null));
        page.ReadAcknowledged = true; await page.ReadAsync();
        Assert.Contains("Collected", page.Outcome); Assert.Contains("Checked successfully", page.SectionSummary); Assert.Equal(1, page.SelectedTab);
        Assert.Equal(2, f.Handler.Calls); Assert.False(page.ReadAcknowledged);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(f.Root.Paths.TenantDirectory(TestData.TenantA), "report-evidence")));
        var evidence = ApplicationReportEvidenceSchema.Read(File.ReadAllText(file), TestData.TenantA);
        Assert.Equal(id, evidence.ReportId); Assert.Equal("live", evidence.SourceMode);
        Assert.Equal(f.Workspace.Session.AccountObjectId, evidence.AccountObjectId);
        if (evidence is ApplicationExpiryEvidence expiry) Assert.Equal(30, expiry.Parameters.HorizonDays);
        else Assert.DoesNotContain("horizonDays", ApplicationReportEvidenceSchema.Serialise(evidence));
        page.RefreshStored(); page.SelectedStored = Assert.Single(page.StoredReports); page.OpenStored();
        await page.ExportAsync(ExportFormat.Json);
        Assert.Equal(ApplicationReportEvidenceSchema.Serialise(evidence), File.ReadAllText(Assert.Single(Directory.GetFiles(f.Root.Paths.ReportsDirectory, "*.json"))).TrimStart('\uFEFF'));
        Assert.Equal(state, ToolkitJson.Serialize(new { f.Workspace.Snapshot, f.Workspace.Plan, f.Workspace.AcknowledgedSnapshotId }));
    }
    [Theory]
    [InlineData("")] [InlineData("0")] [InlineData("366")] [InlineData("1.5")] [InlineData("+30")] [InlineData(" 30")]
    public async Task Invalid_horizon_refuses_without_tokens_and_drops_acknowledgement(string value)
    {
        using var f = new ConnectedReportsFixture(); var page = Page(f); page.ReadAcknowledged = true; page.HorizonDays = value;
        Assert.False(page.ReadAcknowledged); page.ReadAcknowledged = true;
        Assert.Contains("1 to 365", page.ReadGuidance); Assert.False(page.ReadCommand.CanExecute(null));
        await Assert.ThrowsAnyAsync<ToolkitException>(page.ReadAsync); Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }
    [Fact]
    public async Task Typed_horizon_is_frozen_and_consent_has_no_date_or_horizon_request()
    {
        using var f = new ConnectedReportsFixture(); f.Workspace.Session!.Scopes.Add(ApplicationReportRegistry.ApplicationScope);
        var parameters = new ApplicationExpiryParameters { HorizonDays = 1 };
        f.Handler.BeforeResponse = () => parameters.HorizonDays = 365;
        var result = await f.Controller.ReadExpiryAsync(parameters, true, f.Controller.ContextKey);
        Assert.Equal(1, Assert.IsType<ApplicationExpiryEvidence>(result.Evidence.Schema2).Parameters.HorizonDays);
        f.Handler.BeforeResponse = null;
        var page = Page(f, "application-consent"); page.HorizonDays = "not a consent parameter"; page.ReadAcknowledged = true;
        Assert.Empty(page.ReadGuidance); await page.ReadAsync(); Assert.Contains("Not applicable", page.Details);
        page.StartUtc = "2026-09-01T00:00:00Z"; page.ReadAcknowledged = true;
        var calls = f.Handler.Calls; await Assert.ThrowsAnyAsync<ToolkitException>(page.ReadAsync); Assert.Equal(calls, f.Handler.Calls);
    }
    [Theory]
    [InlineData("application-expiry")] [InlineData("application-consent")]
    public async Task Absent_scopes_are_NotAttempted_without_authentication_or_requests(string id)
    {
        using var f = new ConnectedReportsFixture(); var page = Page(f, id); f.Workspace.Session!.Scopes.Clear(); page.Refresh(); page.ReadAcknowledged = true;
        await page.ReadAsync(); Assert.Contains("NotAttempted", page.Outcome); Assert.Contains("not an empty successful check", page.SectionSummary);
        Assert.Equal(0, f.Tokens.Calls); Assert.Equal(0, f.Handler.Calls);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Consent_without_directory_scope_keeps_metadata_and_assignments_but_never_reads_grants(bool directoryOnly)
    {
        using var f = new ConnectedReportsFixture(); ConfigureConsent(f);
        var page = Page(f, "application-consent");
        if (directoryOnly) { f.Workspace.Session!.Scopes = [ApplicationReportRegistry.DirectoryScope]; page.Refresh(); }
        page.ReadAcknowledged = true; await page.ReadAsync();
        Assert.Equal(directoryOnly, f.Handler.Routes.Any(r => r.Contains("oauth2PermissionGrants", StringComparison.Ordinal)));
        Assert.Contains(f.Handler.Routes, r => r.Contains("/appRoleAssignments", StringComparison.Ordinal));
        page.SelectedSection = "app-role-assignments"; Assert.Equal(2, page.Rows!.Count);
        page.SelectedSection = "delegated-grants";
        if (!directoryOnly) { Assert.Contains("NotAttempted", page.SectionSummary); Assert.Contains("Directory.Read.All", page.SectionSummary); }
        page.SelectedSection = "Role definitions"; Assert.True(page.Rows!.Count >= 2);
    }
    [Theory]
    [InlineData("account")] [InlineData("scope")] [InlineData("timestamp")] [InlineData("connection")]
    [InlineData("profile")] [InlineData("selection")] [InlineData("horizon")] [InlineData("tenant")]
    public async Task Actual_response_context_or_selection_change_cannot_be_saved_or_relabelled(string change)
    {
        using var f = new ConnectedReportsFixture(); var page = Page(f); page.ReadAcknowledged = true;
        f.Handler.BeforeResponse = () =>
        {
            switch (change)
            {
                case "account": f.Workspace.Session!.AccountObjectId = TestData.Emergency; break;
                case "scope": f.Workspace.Session!.Scopes.Clear(); break;
                case "timestamp": f.Workspace.Session!.TokenExpiresAt = "2020-01-01T00:00:00Z"; break;
                case "connection": f.ReplaceConnection(); break;
                case "profile": f.Set(nameof(BDIT.TenantToolkit.App.Services.Workspace.Profile), TestData.Profile()); break;
                case "selection": page.SelectedReport = GraphReportPresentation.Find(2, "application-consent"); break;
                case "horizon": page.HorizonDays = "31"; break;
                case "tenant": f.Workspace.Session!.TenantId = TestData.TenantB; break;
            }
        };
        await Assert.ThrowsAnyAsync<ToolkitException>(page.ReadAsync);
        Assert.True(f.Handler.Calls > 0); Assert.False(page.ExportJsonCommand.CanExecute(null)); Assert.Null(page.Rows);
        Assert.False(Directory.Exists(Path.Combine(f.Root.Paths.TenantDirectory(TestData.TenantA), "report-evidence")));
    }
    [Theory]
    [InlineData("ack")] [InlineData("unverified")] [InlineData("expiry")] [InlineData("busy")]
    public async Task Direct_application_read_refuses_invalid_context_before_requests(string fault)
    {
        using var f = new ConnectedReportsFixture(); f.Workspace.Session!.Scopes.Add(ApplicationReportRegistry.ApplicationScope);
        if (fault == "unverified") f.Workspace.Session.OperatorVerified = false;
        if (fault == "expiry") f.Workspace.Session.TokenExpiresAt = "malformed";
        Task Read() => f.Controller.ReadExpiryAsync(new(), fault != "ack", f.Controller.ContextKey);
        if (fault == "busy") await f.Workspace.RunExclusiveAsync("Synthetic busy", async _ => await Assert.ThrowsAnyAsync<ToolkitException>(Read));
        else await Assert.ThrowsAnyAsync<ToolkitException>(Read);
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }
    [Fact]
    public async Task Elapsed_original_timestamp_uses_same_verified_provider_and_saves_a_read()
    {
        using var f = new ConnectedReportsFixture(); f.Workspace.Session!.TokenExpiresAt = "2020-01-01T00:00:00Z";
        var page = Page(f); var session = f.Workspace.Session; var context = f.Controller.ContextKey;
        page.ReadAcknowledged = true; await page.ReadAsync();
        Assert.Equal(2, f.Handler.Calls); Assert.Same(session, f.Workspace.Session); Assert.Equal(context, f.Controller.ContextKey);
        Assert.Contains("saved locally", page.Outcome);
    }
    [Theory]
    [InlineData("failed", "Failed")] [InlineData("cancelled", "Cancelled")]
    public async Task Failed_or_cancelled_application_reads_preserve_honest_states(string fault, string state)
    {
        using var f = new ConnectedReportsFixture(); var page = Page(f);
        f.Handler.Response = (_, ct) =>
        {
            if (fault == "cancelled") { f.Workspace.CancelOperation(); ct.ThrowIfCancellationRequested(); }
            return ConnectedReportsFixture.ReportHttp.Json("{\"error\":{\"code\":\"Denied\",\"message\":\"Synthetic denied\"}}", HttpStatusCode.Forbidden);
        };
        page.ReadAcknowledged = true; await page.ReadAsync(); Assert.Contains(state, page.Outcome);
        Assert.Contains("not an empty successful check", page.SectionSummary);
        page.RefreshStored(); page.SelectedStored = Assert.Single(page.StoredReports); page.OpenStored(); Assert.Contains(state, page.SectionSummary);
    }
    [Theory]
    [InlineData("expiry")] [InlineData("consent")]
    public async Task All_nested_tables_are_readable_and_filters_never_shrink_original_exports(string family)
    {
        using var f = new ConnectedReportsFixture(false);
        ApplicationReportEvidence report = family == "expiry" ? ApplicationReportUiFixtures.Expiry(TestData.TenantA, TestData.Operator, "synthetic")
            : ApplicationReportUiFixtures.Consent(TestData.TenantA, TestData.Operator, "synthetic");
        var file = f.Workspace.Evidence.SaveApplicationReport(report); var bytes = File.ReadAllBytes(file); var page = f.Page(); page.OpenSupplied(file);
        var expected = ApplicationReportDocuments.Sheets(report, true);
        Assert.Equal(expected.Skip(1).Select(s => s.Name), page.Sections);
        foreach (var sheet in expected.Skip(1))
        {
            page.RowState = ReportReadState.Failed; page.SelectedSection = sheet.Name;
            if (!sheet.Rows[0].Contains("Read status"))
            {
                Assert.Equal("All", page.RowState); Assert.False(page.CanFilterReadState);
                Assert.Equal(sheet.Rows.Count - 1, page.Rows!.Count); Assert.Contains("parent/source", page.ReadStateFilterGuidance);
                Assert.Contains("does not prove a successful check", page.SectionSummary);
            }
            page.RowState = "All";
            if (page.Rows!.Count > 0) { page.SelectedRow = page.Rows[0]; Assert.Contains(sheet.Rows[1][0], page.SelectedRowDetails); }
        }
        page.Search = "never matches this synthetic fixture"; Assert.Empty(page.Rows!.Cast<System.Data.DataRowView>());
        foreach (var format in new[] { ExportFormat.Html, ExportFormat.Json, ExportFormat.Csv, ExportFormat.Xlsx }) await page.ExportAsync(format);
        var json = File.ReadAllText(Assert.Single(Directory.GetFiles(f.Root.Paths.ReportsDirectory, "*.json"))).TrimStart('\uFEFF');
        Assert.Equal(ApplicationReportEvidenceSchema.Serialise(report), json);
        var marker = family == "expiry" ? "Synthetic credential 1" : "Synthetic.Write";
        foreach (var exported in Directory.GetFiles(f.Root.Paths.ReportsDirectory).Where(p => !p.EndsWith(".json", StringComparison.Ordinal)))
        {
            string text;
            if (exported.EndsWith(".html", StringComparison.Ordinal)) text = File.ReadAllText(exported);
            else { using var archive = ZipFile.OpenRead(exported); text = string.Join("\n", archive.Entries.Select(e => { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); })); }
            Assert.Contains(marker, text); Assert.Contains(RegisteredReportDocuments.SuppliedFileNotice, text);
        }
        Assert.Equal(bytes, File.ReadAllBytes(file)); Assert.Equal(0, f.Tokens.Calls); Assert.Equal(0, f.Handler.Calls);
    }
    [Theory]
    [InlineData("horizon")] [InlineData("selection")] [InlineData("context")]
    public async Task Change_while_export_busy_cannot_attribute_original_output_to_current_selection(string change)
    {
        using var f = new ConnectedReportsFixture(false); var page = f.Page();
        var file = f.Workspace.Evidence.SaveApplicationReport(ApplicationReportUiFixtures.Expiry(TestData.TenantA, TestData.Operator, "synthetic"));
        page.OpenSupplied(file);
        f.Workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(BDIT.TenantToolkit.App.Services.Workspace.Busy) || !f.Workspace.Busy) return;
            if (change == "horizon") page.HorizonDays = "31";
            if (change == "selection") page.SelectedReport = GraphReportPresentation.Find(2, "application-consent");
            if (change == "context") f.Set(nameof(BDIT.TenantToolkit.App.Services.Workspace.Profile), TestData.Profile(TestData.TenantB));
        };
        await Assert.ThrowsAnyAsync<ToolkitException>(() => page.ExportAsync(ExportFormat.Json));
        Assert.Empty(page.LastExport); Assert.False(page.OpenExportCommand.CanExecute(null));
    }
    [Fact]
    public async Task Save_failure_keeps_a_truthful_unsaved_application_result_and_never_overwrites_history()
    {
        using var f = new ConnectedReportsFixture(); var page = Page(f);
        var folder = Path.Combine(f.Root.Paths.TenantDirectory(TestData.TenantA), "report-evidence");
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!); File.WriteAllText(folder, "synthetic obstruction");
        page.ReadAcknowledged = true; await page.ReadAsync(); Assert.Contains("NOT SAVED", page.Outcome); Assert.True(page.ExportJsonCommand.CanExecute(null));
        File.Delete(folder);
        var result = await f.Controller.ReadExpiryAsync(new(), true, f.Controller.ContextKey); var before = File.ReadAllBytes(result.File);
        Assert.ThrowsAny<ToolkitException>(() => f.Workspace.Evidence.SaveApplicationReport(result.Evidence.Schema2!));
        Assert.Equal(before, File.ReadAllBytes(result.File));
    }
    [Theory]
    [InlineData("https://graph.microsoft.com/v1.0/oauth2PermissionGrants")]
    [InlineData("https://graph.microsoft.com/beta/applications")]
    [InlineData("https://graph.microsoft.com/v1.0/applications/22222222-2222-4222-8222-222222222222/owners")]
    public async Task Actual_application_read_rejects_next_links_into_other_families_versions_or_children(string next)
    {
        using var f = new ConnectedReportsFixture(); var page = Page(f);
        f.Handler.Response = (call, _) => ConnectedReportsFixture.ReportHttp.Json(call == 1
            ? new JsonObject { ["value"] = new JsonArray(), ["@odata.nextLink"] = next }.ToJsonString() : "{\"value\":[]}");
        page.ReadAcknowledged = true; await page.ReadAsync();
        Assert.Contains("Partial", page.Outcome); Assert.Equal(2, f.Handler.Calls);
        Assert.DoesNotContain(next, f.Handler.Routes);
        Assert.All(f.Handler.Routes, route => Assert.Contains("/v1.0/", route));
    }

    private static void ConfigureConsent(ConnectedReportsFixture f)
    {
        f.Handler.Response = (_, _) =>
        {
            var path = new Uri(f.Handler.Routes.Last()).AbsolutePath;
            var items = new JsonArray();
            if (path == "/v1.0/servicePrincipals")
                foreach (var id in new[] { TestData.Operator, TestData.Mam }) items.Add(new JsonObject { ["id"] = id, ["displayName"] = "Same synthetic name", ["appId"] = TestData.ClientId,
                    ["appRoles"] = new JsonArray(new JsonObject { ["id"] = TestData.Office, ["displayName"] = "Synthetic role", ["value"] = "Synthetic.Read", ["isEnabled"] = true }) });
            if (path.EndsWith("/appRoleAssignments", StringComparison.Ordinal))
            {
                var principal = path.Split('/')[3];
                items.Add(new JsonObject { ["id"] = "assignment_" + principal, ["principalId"] = principal, ["resourceId"] = TestData.Mam, ["appRoleId"] = TestData.Office });
            }
            return ConnectedReportsFixture.ReportHttp.Json(new JsonObject { ["value"] = items }.ToJsonString());
        };
    }
}
