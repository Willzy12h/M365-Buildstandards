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
    private sealed class Source(SecurityIncidentReadSelection selection, Action before, bool withRow = false) : ISecurityIncidentReportSource
    {
        public string TenantId => TestData.TenantA;
        public SecurityIncidentReadSelection IncidentSelection => selection;
        public Task<OperationalReportPage> ReadIncidentsNextAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); before(); return Task.FromResult(new OperationalReportPage(withRow ? [new System.Text.Json.Nodes.JsonObject {
            ["id"] = "123", ["displayName"] = "Synthetic incident", ["createdDateTime"] = "2026-10-19T09:00:00Z", ["lastUpdateDateTime"] = "2026-10-19T10:00:00Z",
            ["severity"] = "medium", ["status"] = "active", ["classification"] = "unknown", ["determination"] = "unknown", ["priorityScore"] = null,
            ["redirectIncidentId"] = null, ["incidentWebUrl"] = null }] : [], false)); }
    }
    private static OperationalReportsViewModel Page(ConnectedReportsFixture f, Action before, bool withRow = false)
    {
        f.Workspace.Session!.Scopes.Add("SecurityIncident.Read.All");
        Func<ConnectedTenant, SecurityIncidentReadSelection, Action, ISecurityIncidentReportSource> factory = (_, selection, _) => new Source(selection, before, withRow);
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
    [Fact] public async Task Native_incident_reference_round_trips_exact_original_without_assessment_authority()
    {
        using var f = new ConnectedReportsFixture(); var page = Page(f, () => { }, withRow: true);
        page.Acknowledged = true; await page.ReadAsync(); await page.SavePendingOriginalAsync();
        var source = OperationalContextFile.ReadFile(page.LastExport, TestData.TenantA);
        var references = Property(page, "IncidentReferences").GetValue(page)!;
        Set(references, "SelectedIncidentId", "123"); Set(references, "SelectedControlId", f.Workspace.Standard!.Controls[0].Id);
        var add = references.GetType().GetMethod("AddReference"); Assert.NotNull(add); add.Invoke(references,null);
        Assert.Equal(1, Property(references,"ReferenceCount").GetValue(references));
        var save = references.GetType().GetMethod("ExportReferencesAsync"); Assert.NotNull(save); await (Task)save.Invoke(references,null)!;
        var file = (string)Property(references,"LastExport").GetValue(references)!;
        var link = Assert.Single(IncidentControlReferenceDocument.ReadFile(file, source));
        Assert.True(IncidentControlReferences.Resolve(f.Root.Paths,link,source,f.Workspace.Standard).Resolved);
        Assert.Equal(0,f.Tokens.Calls); Assert.Equal(0,f.Handler.Calls); Assert.Null(f.Workspace.Snapshot);
    }
    [Fact] public async Task Client_change_blocks_new_incident_links_without_losing_unsaved_source_bound_references()
    {
        using var f = new ConnectedReportsFixture(); var page = Page(f, () => { }, withRow: true);
        page.Acknowledged = true; await page.ReadAsync(); await page.SavePendingOriginalAsync();
        var references = Property(page,"IncidentReferences").GetValue(page)!;
        Set(references,"SelectedIncidentId","123"); Set(references,"SelectedControlId",f.Workspace.Standard!.Controls[0].Id);
        var add = references.GetType().GetMethod("AddReference"); Assert.NotNull(add); add.Invoke(references,null);
        f.ReplaceConnection(); page.Refresh();
        var refused = Assert.Throws<TargetInvocationException>(() => add.Invoke(references,null)); Assert.IsAssignableFrom<ToolkitException>(refused.InnerException);
        Assert.Equal(1,Property(references,"ReferenceCount").GetValue(references));
        var save = references.GetType().GetMethod("ExportReferencesAsync"); Assert.NotNull(save); await (Task)save.Invoke(references,null)!;
        Assert.True(File.Exists((string)Property(references,"LastExport").GetValue(references)!));
        Assert.Equal(0,f.Tokens.Calls); Assert.Equal(0,f.Handler.Calls);
    }
}
