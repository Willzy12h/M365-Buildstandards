using System.Text.Json.Nodes;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class MailboxCapacityPageTests
{
    [Fact]
    public async Task Collection_shortcuts_navigate_without_authentication_evidence_selection_or_busy_execution()
    {
        using var f = new ConnectedReportsFixture(false);
        var shell = new ShellViewModel(f.Workspace); var page = shell.Page<MailboxCapacityViewModel>();
        shell.Navigate("mailbox-capacity");
        var profile = f.Workspace.Profile; var standard = f.Workspace.Standard;
        page.OpenMailboxCollectionCommand.Execute(null);
        var run = Assert.IsType<RunReportsViewModel>(shell.CurrentPage);
        Assert.False(run.ExperimentalReadEnabled); Assert.False(run.HasReview); Assert.False(run.Approved);
        shell.Navigate("mailbox-capacity"); page.OpenLicenceCollectionCommand.Execute(null);
        Assert.IsType<GraphReportsViewModel>(shell.CurrentPage);
        Assert.Null(page.SelectedMailbox); Assert.Null(page.SelectedLicence); Assert.Empty(page.Rows);
        Assert.Contains("No source is selected automatically", page.CollectionGuidance);
        await f.Workspace.RunExclusiveAsync("synthetic active task", _ =>
        {
            var current = shell.CurrentPage;
            Assert.False(page.OpenMailboxCollectionCommand.CanExecute(null));
            Assert.False(page.OpenLicenceCollectionCommand.CanExecute(null));
            page.OpenMailboxCollectionCommand.Execute(null); page.OpenLicenceCollectionCommand.Execute(null);
            Assert.Same(current, shell.CurrentPage);
            return Task.CompletedTask;
        });
        Assert.Same(profile, f.Workspace.Profile); Assert.Same(standard, f.Workspace.Standard);
        Assert.Null(f.Workspace.Snapshot); Assert.Null(f.Workspace.Plan); Assert.Null(f.Workspace.Connection);
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls); Assert.False(shell.HasError);
    }

    private static string Fixture(string family) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../build/test-fixtures/mailbox-capacity-" + family + ".json"));
    private static MailboxCapacityViewModel Supplied(ConnectedReportsFixture f)
    {
        var p = new MailboxCapacityViewModel(new ShellViewModel(f.Workspace)) { MailboxMode = "Supplied file", LicenceMode = "Supplied file", MailboxFile = Fixture("mailboxes"), LicenceFile = Fixture("licences") };
        return p;
    }
    [Fact]
    public async Task Supplied_review_keeps_unknown_details_and_filtered_export_keeps_full_sources_without_collection()
    {
        using var f = new ConnectedReportsFixture(false); var page = Supplied(f);
        var originals = File.ReadAllBytes(Fixture("mailboxes")); page.Review();
        Assert.Equal(1, page.SelectedTab); Assert.Equal(2, page.Rows.Count); Assert.Contains("historical", page.Notice);
        page.SelectedRow = page.Rows[1]; Assert.Contains("Unlimited", page.SelectedDetails); Assert.Contains("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", page.SelectedDetails);
        page.Search = "Synthetic user"; Assert.Single(page.Rows); Assert.Contains("1 of 2", page.FilterSummary);
        await page.ExportAsync(ExportFormat.Json);
        var path = Assert.Single(Directory.GetFiles(f.Root.Paths.ReportsDirectory)); var json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal(2, json["review"]!["mailboxes"]!.AsArray().Count);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(Fixture("mailboxes"))), json["originalMailboxSource"]));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(Fixture("licences"))), json["originalLicenceSource"]));
        Assert.Equal(originals, File.ReadAllBytes(Fixture("mailboxes"))); Assert.Null(f.Workspace.Snapshot);
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }
    [Fact]
    public void Saved_pair_and_damaged_selection_notify_reason_and_direct_method_refuses()
    {
        using var f = new ConnectedReportsFixture(false);
        var m = ExchangeReportEvidenceSchema.Read(File.ReadAllText(Fixture("mailboxes")), TestData.TenantA);
        var l = ReportEvidenceSchema.Read(File.ReadAllText(Fixture("licences")), TestData.TenantA);
        var file = f.Workspace.Evidence.SaveExchangeReport(m); f.Workspace.Evidence.SaveReport(l);
        var id = Guid.NewGuid().ToString("D"); File.WriteAllText(Path.Combine(Path.GetDirectoryName(file)!, id + ".json"), "{} ");
        var page = new MailboxCapacityViewModel(new ShellViewModel(f.Workspace)); page.RefreshSources();
        page.SelectedMailbox = page.MailboxRecords.Single(e => e.CanOpen); page.SelectedLicence = Assert.Single(page.LicenceRecords); page.Review(); Assert.Equal(2, page.Rows.Count);
        var notifications = new List<string?>(); page.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        page.SelectedMailbox = page.MailboxRecords.Single(e => !e.CanOpen);
        Assert.Contains(nameof(page.SelectedMailbox), notifications); Assert.Contains(nameof(page.ReviewGuidance), notifications);
        Assert.Empty(page.Rows); Assert.Equal("No accepted source pair.", page.Provenance); Assert.NotEmpty(page.SelectedMailbox.Problem);
        Assert.False(page.ReviewCommand.CanExecute(null)); Assert.Throws<ToolkitException>(page.Review);
        Assert.Contains(page.SelectedMailbox.Problem, page.ReviewGuidance); Assert.Equal(0, f.Handler.Calls);
    }
    [Theory]
    [InlineData("mailboxFile")] [InlineData("licenceFile")] [InlineData("mode")] [InlineData("context")]
    public async Task Changing_selection_or_context_invalidates_result_export_and_direct_invocation(string change)
    {
        using var f = new ConnectedReportsFixture(false); var page = Supplied(f); page.Review();
        if (change == "mailboxFile") page.MailboxFile = "different.json";
        if (change == "licenceFile") page.LicenceFile = "different.json";
        if (change == "mode") page.MailboxMode = "Saved record";
        if (change == "context") { f.Set(nameof(f.Workspace.Profile), TestData.Profile(TestData.TenantB)); page.Refresh(); }
        Assert.Empty(page.Rows); Assert.Empty(page.LastExport); Assert.False(page.ExportHtmlCommand.CanExecute(null));
        await Assert.ThrowsAnyAsync<ToolkitException>(() => page.ExportAsync(ExportFormat.Html)); Assert.Empty(Directory.GetFiles(f.Root.Paths.ReportsDirectory));
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }
    [Fact]
    public async Task Busy_event_selection_change_refuses_export_before_write_and_never_rebinds_original_output()
    {
        using var f = new ConnectedReportsFixture(false); var page = Supplied(f); page.Review();
        f.Workspace.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(f.Workspace.Busy) && f.Workspace.Busy) page.MailboxFile = "changed-during-export.json"; };
        await Assert.ThrowsAnyAsync<ToolkitException>(() => page.ExportAsync(ExportFormat.Json));
        Assert.Empty(page.LastExport); Assert.Empty(page.Rows); Assert.Empty(Directory.GetFiles(f.Root.Paths.ReportsDirectory));
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }
    [Fact]
    public void Failed_repeat_review_clears_accepted_summary_provenance_and_notifies_bindings()
    {
        using var f = new ConnectedReportsFixture(false); var page = Supplied(f); page.Review();
        var damaged = Path.Combine(f.Root.Root, "damaged.json"); File.WriteAllText(damaged, "{}"); page.MailboxFile = damaged;
        var notices = new List<string?>(); page.PropertyChanged += (_, a) => notices.Add(a.PropertyName);
        page.ReviewCommand.Execute(null);
        Assert.True(page.Shell.HasError); Assert.Empty(page.Rows); Assert.Equal("No accepted source pair.", page.Provenance);
        Assert.Equal("No accepted mailbox capacity review.", page.Summary); Assert.Contains(nameof(page.Provenance), notices); Assert.Contains(nameof(page.Summary), notices);
    }
}
