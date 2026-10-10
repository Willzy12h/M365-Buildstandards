using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

internal sealed class ConnectedReportsFixture : IDisposable
{
    public TempRoot Root { get; } = new();
    public ToolkitLogger Logger { get; }
    public Workspace Workspace { get; }
    public ReportHttp Handler { get; } = new();
    public ReportTokens Tokens { get; } = new();
    private readonly HttpClient _http;
    public GraphReportsController Controller { get; }
    public ConnectedReportsFixture(bool connect = true)
    {
        Root.WriteStandard("test.json", TestData.StandardJson); Root.WriteManifest();
        Logger = new(Root.Paths.LogsDirectory, LogLevel.Debug);
        Workspace = new(Root.Paths, new ToolkitSettings(), Logger, diagnostics: true); Workspace.Initialise();
        Workspace.ApplyProfileToSession(TestData.Profile(), save: false);
        _http = new(Handler);
        if (connect) ReplaceConnection();
        Controller = new(Workspace);
    }
    public void ReplaceConnection()
    {
        var s = TestData.Session(mode: SessionMode.Assessment);
        s.TokenExpiresAt = Timestamps.Format(DateTimeOffset.UtcNow.AddHours(1));
        s.Scopes = ["Organization.Read.All", "User.Read.All", "DeviceManagementManagedDevices.Read.All", "AuditLog.Read.All"];
        var graph = new GraphClient(_http, Tokens, s.TenantId, s.Mode, GraphRouteAllowList.FromStandard(Workspace.RequireStandard()),
            new GraphClientOptions { Sleep = false }, NullLog.Instance);
        Set(nameof(Workspace.Connection), new ConnectedTenant(s, graph, null));
    }
    public void Set(string property, object? value) => typeof(Workspace).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(Workspace, value);
    public GraphReportsViewModel Page() => new(new ShellViewModel(Workspace));
    public Task<GraphReportsController.Outcome> Read(string id = "intune-devices") => Controller.ReadAsync(id,
        GraphReportRegistry.Find(id).DateRange ? new() { Start = "2026-09-01T00:00:00Z", End = "2026-09-02T00:00:00Z" } : new(), true, Controller.ContextKey);
    public void Dispose() { _http.Dispose(); Logger.Dispose(); Root.Dispose(); }
    public static JsonObject Device(int index = 1) => new()
    {
        ["id"] = new Guid(index, 0, 0, new byte[8]).ToString(), ["deviceName"] = "Synthetic device " + index,
        ["managedDeviceOwnerType"] = "company", ["operatingSystem"] = "Windows", ["complianceState"] = "compliant",
        ["lastSyncDateTime"] = "2026-09-01T01:00:00Z", ["customSecretField"] = "must-never-project"
    };
    internal sealed class ReportTokens : IAccessTokenProvider
    {
        public int Calls { get; private set; }
        public Task<string> GetAccessTokenAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); Calls++; return Task.FromResult("synthetic-token"); }
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => GetAccessTokenAsync(ct);
    }
    internal sealed class ReportHttp : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Func<int, CancellationToken, HttpResponseMessage>? Response { get; set; }
        public Action? BeforeResponse { get; set; }
        public List<string> Routes { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, request.Method); Calls++; Routes.Add(request.RequestUri!.AbsoluteUri); BeforeResponse?.Invoke();
            return Task.FromResult(Response?.Invoke(Calls, ct) ?? Json("{\"value\":[]}"));
        }
        public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

public sealed class GraphReportsPageTests
{
    [Theory]
    [InlineData("users-licences", 2)]
    [InlineData("intune-devices", 1)]
    [InlineData("mfa-registration", 1)]
    [InlineData("sign-ins", 1)]
    [InlineData("directory-audit", 1)]
    public async Task Registered_reads_save_genuine_zero_without_changing_deployment_evidence(string id, int requests)
    {
        using var f = new ConnectedReportsFixture();
        var capture = TestData.Snapshot(f.Workspace.RequireStandard());
        f.Set(nameof(Workspace.Snapshot), capture); f.Set(nameof(Workspace.AcknowledgedSnapshotId), capture.Id);
        var before = ToolkitJson.Serialize(f.Workspace.Snapshot);
        var page = f.Page(); page.SelectedReport = GraphReportRegistry.Find(id);
        if (page.RequiresDates) { page.StartUtc = "2026-09-01T00:00:00Z"; page.EndUtc = "2026-09-02T00:00:00Z"; }
        page.OnNavigatedTo(); page.RefreshStored();
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
        Assert.False(page.ReadCommand.CanExecute(null)); page.ReadAcknowledged = true;
        await page.ReadAsync();
        Assert.Equal(requests, f.Handler.Calls);
        Assert.Contains("Collected", page.Outcome); Assert.Contains("saved locally", page.Outcome);
        Assert.Contains("Checked successfully; no objects returned", page.SectionSummary);
        Assert.False(page.ReadAcknowledged);
        Assert.Equal(before, ToolkitJson.Serialize(f.Workspace.Snapshot)); Assert.Equal(capture.Id, f.Workspace.AcknowledgedSnapshotId); Assert.Null(f.Workspace.Plan);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(f.Root.Paths.TenantDirectory(TestData.TenantA), "report-evidence")));
        var report = ReportEvidenceSchema.Read(File.ReadAllText(file), TestData.TenantA);
        Assert.Equal(ReportReadState.Collected, report.Status);
        Assert.All(report.Sections, s => Assert.Empty(s.Rows));
        page.RefreshStored(); page.SelectedStored = Assert.Single(page.StoredReports); page.OpenStored();
        await page.ExportAsync(ExportFormat.Json);
        Assert.Equal(requests, f.Handler.Calls);
        var exported = Assert.Single(Directory.GetFiles(f.Root.Paths.ReportsDirectory, "*.json"));
        Assert.Equal(ReportEvidenceSchema.Serialize(report), File.ReadAllText(exported).TrimStart('\uFEFF'));
    }

    [Fact]
    public async Task Missing_scope_produces_NotAttempted_without_token_or_report_requests()
    {
        using var f = new ConnectedReportsFixture(); f.Workspace.Session!.Scopes.Clear();
        var page = f.Page(); page.SelectedReport = GraphReportRegistry.Find("mfa-registration");
        Assert.Contains("unavailable", page.ScopeExplanation); page.ReadAcknowledged = true;
        await page.ReadAsync();
        Assert.Contains("NotAttempted", page.Outcome); Assert.Contains("not an empty successful check", page.SectionSummary);
        Assert.Equal(0, f.Tokens.Calls); Assert.Equal(0, f.Handler.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("unverified")]
    [InlineData("wrongTenant")]
    [InlineData("wrongAccount")]
    [InlineData("fakeGraph")]
    [InlineData("unacknowledged")]
    public async Task Direct_read_guards_refuse_invalid_context_before_any_tenant_request(string fault)
    {
        using var f = new ConnectedReportsFixture(connect: fault != "missing");
        if (fault == "expired") f.Workspace.Session!.TokenExpiresAt = "2020-01-01T00:00:00Z";
        if (fault == "unverified") f.Workspace.Session!.TenantVerified = false;
        if (fault == "wrongTenant") f.Workspace.Session!.TenantId = TestData.TenantB;
        if (fault == "wrongAccount") f.Workspace.Session!.AccountObjectId = "invalid";
        if (fault == "fakeGraph") f.Set(nameof(Workspace.Connection), new ConnectedTenant(f.Workspace.Session!, new FakeGraphClient(TestData.Standard()), null));
        await Assert.ThrowsAnyAsync<ToolkitException>(() => f.Controller.ReadAsync("intune-devices", new(), fault != "unacknowledged", f.Controller.ContextKey));
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
        Assert.False(Directory.Exists(Path.Combine(f.Root.Paths.TenantDirectory(TestData.TenantA), "report-evidence")));
    }

    [Theory]
    [InlineData("sign-ins", null, null)]
    [InlineData("directory-audit", "2026-09-02T00:00:00Z", "2026-09-01T00:00:00Z")]
    [InlineData("sign-ins", "2026-01-01T00:00:00Z", "2026-03-01T00:00:00Z")]
    [InlineData("sign-ins", "2099-01-01T00:00:00Z", "2099-01-02T00:00:00Z")]
    [InlineData("intune-devices", "2026-09-01T00:00:00Z", "2026-09-02T00:00:00Z")]
    public async Task Invalid_or_inapplicable_dates_refuse_before_requests(string id, string? start, string? end)
    {
        using var f = new ConnectedReportsFixture();
        await Assert.ThrowsAnyAsync<ToolkitException>(() => f.Controller.ReadAsync(id, new() { Start = start, End = end }, true, f.Controller.ContextKey));
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }

    [Theory]
    [InlineData("failed", "Failed", 0)]
    [InlineData("partial", "Partial", 1)]
    [InlineData("cancelled-empty", "Cancelled", 0)]
    [InlineData("cancelled-rows", "Cancelled", 1)]
    public async Task Failed_partial_and_cancelled_states_survive_store_reopen_and_export(string mode, string expected, int rows)
    {
        using var f = new ConnectedReportsFixture();
        f.Handler.Response = (call, ct) =>
        {
            if (call == 1 && mode is "partial" or "cancelled-rows")
                return ConnectedReportsFixture.ReportHttp.Json(new JsonObject { ["value"] = new JsonArray(ConnectedReportsFixture.Device()), ["@odata.nextLink"] = "https://graph.microsoft.com/v1.0/deviceManagement/managedDevices?$skiptoken=synthetic" }.ToJsonString());
            if (mode.StartsWith("cancelled", StringComparison.Ordinal)) { f.Workspace.CancelOperation(); ct.ThrowIfCancellationRequested(); }
            return ConnectedReportsFixture.ReportHttp.Json("{\"error\":{\"code\":\"Denied\",\"message\":\"Synthetic denial\"}}", HttpStatusCode.Forbidden);
        };
        var page = f.Page(); page.SelectedReport = GraphReportRegistry.Find("intune-devices"); page.ReadAcknowledged = true;
        await page.ReadAsync(); Assert.Contains(expected, page.Outcome); Assert.Equal(rows, page.Rows!.Count);
        if (rows == 0) Assert.Contains("not an empty successful check", page.SectionSummary);
        page.RefreshStored(); page.SelectedStored = Assert.Single(page.StoredReports); page.OpenStored();
        Assert.Contains(expected, page.SectionSummary); Assert.Equal(rows, page.Rows!.Count);
        await page.ExportAsync(ExportFormat.Html);
        var html = File.ReadAllText(Assert.Single(Directory.GetFiles(f.Root.Paths.ReportsDirectory, "*.html")));
        Assert.Contains(expected, html); Assert.DoesNotContain("must-never-project", html);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("connection")]
    [InlineData("profile")]
    [InlineData("selection")]
    public async Task Mid_read_context_or_selection_change_accepts_and_saves_nothing(string change)
    {
        using var f = new ConnectedReportsFixture(); var page = f.Page(); page.ReadAcknowledged = true;
        f.Handler.BeforeResponse = () =>
        {
            if (change == "account") f.Workspace.Session!.AccountObjectId = TestData.Emergency;
            if (change == "connection") f.ReplaceConnection();
            if (change == "profile") f.Set(nameof(Workspace.Profile), TestData.Profile());
            if (change == "selection") page.SelectedReport = GraphReportRegistry.Find("mfa-registration");
        };
        await Assert.ThrowsAnyAsync<ToolkitException>(page.ReadAsync);
        Assert.False(page.ExportJsonCommand.CanExecute(null)); Assert.False(page.ReadAcknowledged);
        Assert.False(Directory.Exists(Path.Combine(f.Root.Paths.TenantDirectory(TestData.TenantA), "report-evidence")));
    }

    [Fact]
    public async Task Unsaved_result_is_explicit_and_existing_report_collision_preserves_original_bytes()
    {
        using var f = new ConnectedReportsFixture();
        var folder = Path.Combine(f.Root.Paths.TenantDirectory(TestData.TenantA), "report-evidence");
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!); File.WriteAllText(folder, "synthetic path obstruction");
        var page = f.Page(); page.ReadAcknowledged = true; await page.ReadAsync();
        Assert.Contains("NOT SAVED", page.Outcome); Assert.True(page.ExportJsonCommand.CanExecute(null));
        File.Delete(folder);
        var result = await f.Read(); var original = File.ReadAllBytes(result.File);
        Assert.ThrowsAny<ToolkitException>(() => f.Workspace.Evidence.SaveReport(result.Evidence));
        Assert.Equal(original, File.ReadAllBytes(result.File));
    }

    [Fact]
    public async Task Visual_filters_preserve_all_export_rows_and_supplied_provenance_in_every_format()
    {
        using var f = new ConnectedReportsFixture();
        f.Handler.Response = (_, _) => ConnectedReportsFixture.ReportHttp.Json(new JsonObject { ["value"] = new JsonArray(ConnectedReportsFixture.Device(), ConnectedReportsFixture.Device(2)) }.ToJsonString());
        var capture = await f.Read(); var bytes = File.ReadAllBytes(capture.File); var page = f.Page();
        page.OpenSupplied(capture.File); page.Search = "Synthetic device 1";
        Assert.Single(page.Rows!.Cast<System.Data.DataRowView>()); Assert.Contains("display filters only", page.FilterSummary);
        page.SelectedRow = page.Rows![0]; Assert.Contains("Object ID", page.SelectedRowDetails);
        Assert.Contains(RegisteredReportDocuments.SuppliedFileNotice, page.ProvenanceNotice);
        foreach (var format in new[] { ExportFormat.Html, ExportFormat.Json, ExportFormat.Csv, ExportFormat.Xlsx }) await page.ExportAsync(format);
        Assert.Equal(1, f.Handler.Calls); Assert.Equal(bytes, File.ReadAllBytes(capture.File));
        var html = File.ReadAllText(Assert.Single(Directory.GetFiles(f.Root.Paths.ReportsDirectory, "*.html")));
        Assert.Contains("Synthetic device 2", html); Assert.Contains(RegisteredReportDocuments.SuppliedFileNotice, html);
        var json = ReportEvidenceSchema.Read(File.ReadAllText(Assert.Single(Directory.GetFiles(f.Root.Paths.ReportsDirectory, "*.json"))).TrimStart('\uFEFF'), TestData.TenantA);
        Assert.Equal(2, json.Sections[0].Rows.Count); Assert.Equal(capture.Evidence.IntegrityDigest, json.IntegrityDigest);
        Assert.Single(Directory.GetFiles(f.Root.Paths.ReportsDirectory, "*.csv.zip")); Assert.Single(Directory.GetFiles(f.Root.Paths.ReportsDirectory, "*.xlsx"));
        f.Workspace.Session!.AccountObjectId = TestData.Emergency;
        await Assert.ThrowsAnyAsync<ToolkitException>(() => page.ExportAsync(ExportFormat.Json));
        page.Refresh(); Assert.False(page.ExportHtmlCommand.CanExecute(null)); Assert.False(page.ReadAcknowledged);
    }

    [Fact]
    public async Task Bounded_page_read_retains_cap_and_never_inferrs_completeness()
    {
        using var f = new ConnectedReportsFixture();
        var array = new JsonArray(); for (var i = 1; i <= GraphReportRegistry.MaximumRows + 1; i++) array.Add(ConnectedReportsFixture.Device(i));
        f.Handler.Response = (_, _) => ConnectedReportsFixture.ReportHttp.Json(new JsonObject { ["value"] = array }.ToJsonString());
        var page = f.Page(); page.SelectedReport = GraphReportRegistry.Find("intune-devices"); page.ReadAcknowledged = true;
        await page.ReadAsync(); Assert.Equal(GraphReportRegistry.MaximumRows, page.Rows!.Count);
        Assert.Contains("Partial", page.Outcome); Assert.Contains("cap", page.SectionSummary);
        Assert.Equal(1, f.Handler.Calls);
    }

    [Fact]
    public async Task Direct_page_actions_refuse_busy_workspace_and_new_selection_clears_acknowledgement()
    {
        using var f = new ConnectedReportsFixture(); var page = f.Page(); page.ReadAcknowledged = true;
        page.SelectedReport = GraphReportRegistry.Find("sign-ins"); Assert.False(page.ReadAcknowledged);
        page.StartUtc = "2026-09-01T00:00:00Z"; page.EndUtc = "2026-09-02T00:00:00Z";
        page.ReadAcknowledged = true; page.EndUtc = "2026-09-03T00:00:00Z"; Assert.False(page.ReadAcknowledged);
        page.SelectedReport = GraphReportRegistry.Find("intune-devices"); Assert.Empty(page.StartUtc); Assert.Empty(page.EndUtc);
        page.ReadAcknowledged = true;
        await f.Workspace.RunExclusiveAsync("Synthetic concurrent operation", async _ =>
        {
            await Assert.ThrowsAnyAsync<ToolkitException>(page.ReadAsync);
            Assert.ThrowsAny<ToolkitException>(page.RefreshStored);
            Assert.ThrowsAny<ToolkitException>(() => page.OpenSupplied("never-opened.json"));
        });
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
        await page.ReadAsync(); Assert.Equal(1, page.SelectedTab);
    }

    [Fact]
    public async Task Export_does_not_attribute_output_to_a_selection_changed_during_exclusive_work()
    {
        using var f = new ConnectedReportsFixture(); var saved = await f.Read(); var page = f.Page(); page.OpenSupplied(saved.File);
        f.Workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Workspace.Busy) && f.Workspace.Busy) page.SelectedReport = GraphReportRegistry.Find("mfa-registration");
        };
        await Assert.ThrowsAnyAsync<ToolkitException>(() => page.ExportAsync(ExportFormat.Json));
        Assert.Empty(page.LastExport); Assert.False(page.ExportJsonCommand.CanExecute(null)); Assert.False(page.OpenExportCommand.CanExecute(null));
        Assert.Equal(1, f.Handler.Calls);
        var exported = Assert.Single(Directory.GetFiles(f.Root.Paths.ReportsDirectory, "*.json"));
        Assert.Equal(saved.Evidence.Id, ReportEvidenceSchema.Read(File.ReadAllText(exported).TrimStart('\uFEFF'), TestData.TenantA).Id);
    }
}
