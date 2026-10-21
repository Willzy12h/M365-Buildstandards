using System.Reflection;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using BuildStandard.Graph;
using Xunit;
namespace BuildStandard.Tests;
public sealed class SecurityIncidentReportsPageTests
{
    private static PropertyInfo Property(object value, string name)
    {
        var property = value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property); return property;
    }
    private static void Set(object value, string name, object content) => Property(value, name).SetValue(value, content);
    [Fact] public void Optional_incident_access_defaults_off_and_selection_only_changes_local_intent()
    {
        using var f = new ConnectedReportsFixture(); var shell = new ShellViewModel(f.Workspace);
        var connect = shell.Page<ConnectViewModel>(); var setup = shell.Page<ApplicationSetupViewModel>();
        Assert.False((bool)Property(connect, "IncludeSecurityIncidents").GetValue(connect)!);
        Assert.False((bool)Property(setup, "IncludeSecurityIncidents").GetValue(setup)!);
        Set(connect, "IncludeSecurityIncidents", true); Set(setup, "IncludeSecurityIncidents", true);
        Assert.Contains("SecurityIncident.Read.All", connect.OptionalAccessSummary); Assert.Contains("SecurityIncident.Read.All", setup.OptionalAccessSummary);
        Assert.Equal(0, f.Tokens.Calls); Assert.Equal(0, f.Handler.Calls);
    }
    private sealed class Source(SecurityIncidentReadSelection selection, Action before) : ISecurityIncidentReportSource
    {
        public string TenantId => TestData.TenantA;
        public SecurityIncidentReadSelection IncidentSelection => selection;
        public Task<OperationalReportPage> ReadIncidentsNextAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); before(); return Task.FromResult(new OperationalReportPage([], false)); }
    }
    private static OperationalReportsViewModel Page(ConnectedReportsFixture f, Action before)
    {
        f.Workspace.Session!.Scopes.Add("SecurityIncident.Read.All");
        Func<ConnectedTenant, SecurityIncidentReadSelection, Action, ISecurityIncidentReportSource> factory = (_, selection, _) => new Source(selection, before);
        Set(f.Controller, "IncidentSourceFactory", factory);
        var page = new OperationalReportsViewModel(new ShellViewModel(f.Workspace), f.Controller) { SelectedReportId = "security-incidents" };
        Set(page, "IncidentStartUtc", "2026-10-19T00:00:00Z"); Set(page, "IncidentEndUtc", "2026-10-20T00:00:00Z");
        return page;
    }
    [Fact] public async Task Incident_capture_preserves_original_without_configuration_or_network_authority()
    {
        using var f = new ConnectedReportsFixture(); var reads = 0; var page = Page(f, () => reads++);
        Assert.Contains(page.ReportChoices, choice => choice.Id == "security-incidents");
        Assert.Contains("SecurityIncident.Read.All", page.AccessPreview); Assert.Contains("not proof", page.Notice);
        var snapshot = f.Workspace.Snapshot; page.Acknowledged = true; await page.ReadAsync();
        Assert.True(page.HasPendingOriginal); await page.SavePendingOriginalAsync();
        var saved = OperationalContextFile.ReadFile(page.LastExport, TestData.TenantA);
        Assert.Equal("security-incidents", saved.Report.ReportId); Assert.Empty(saved.Report.Sections[0].Rows);
        Assert.Same(snapshot, f.Workspace.Snapshot); Assert.Equal(1, reads); Assert.Equal(0, f.Tokens.Calls); Assert.Equal(0, f.Handler.Calls);
    }
    [Fact] public async Task Window_change_invalidates_acknowledgement_and_late_selection_is_not_admitted()
    {
        using var f = new ConnectedReportsFixture(); OperationalReportsViewModel? page = null;
        page = Page(f, () => Set(page!, "IncidentEndUtc", "2026-10-20T01:00:00Z"));
        page.Acknowledged = true; Set(page, "IncidentStartUtc", "2026-10-18T00:00:00Z"); Assert.False(page.Acknowledged);
        page.Acknowledged = true; await Assert.ThrowsAnyAsync<ToolkitException>(page.ReadAsync);
        Assert.Contains("No current", page.Outcome); Assert.False(page.HasPendingOriginal);
        Assert.Equal(0, f.Tokens.Calls); Assert.Equal(0, f.Handler.Calls);
    }
}
