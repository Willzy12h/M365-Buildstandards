using System.IO;
using System.Windows;
using System.Windows.Controls;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;

/// <summary>Offline synthetic mailbox journey; root wires this companion into the existing harness after page registration.</summary>
internal static partial class Program
{
    private static string MailboxCapacityFixtureFile = "", MailboxCapacityLicenceFile = "";
    private static void SeedMailboxCapacityEvidence(Workspace workspace, string source)
    {
        const string fixtureTenant = "11111111-1111-4111-8111-111111111111";
        var mailbox = ExchangeReportEvidenceSchema.Read(File.ReadAllText(Path.Combine(source, "build/test-fixtures/mailbox-capacity-mailboxes.json")), fixtureTenant);
        var licences = ReportEvidenceSchema.Read(File.ReadAllText(Path.Combine(source, "build/test-fixtures/mailbox-capacity-licences.json")), fixtureTenant);
        // These are newly invented harness records, not modifications/reseals of supplied user evidence.
        mailbox.TenantId = licences.TenantId = Tenant;
        var unknownArchive = mailbox.Sections[0].Rows[1];
        unknownArchive["archiveReadStatus"] = ReportReadState.Failed; unknownArchive["hasArchive"] = null;
        unknownArchive["readStatus"] = ReportReadState.Partial; unknownArchive["error"] = "Synthetic archive existence could not be checked.";
        mailbox.Status = mailbox.Sections[0].Status = ReportReadState.Partial;
        mailbox.Sections[0].Error = "Synthetic partial inventory; the archive state stays unknown.";
        ExchangeReportEvidenceSchema.Seal(mailbox); ReportEvidenceSchema.Seal(licences);
        MailboxCapacityFixtureFile = workspace.Evidence.SaveExchangeReport(mailbox);
        MailboxCapacityLicenceFile = workspace.Evidence.SaveReport(licences);
    }
    private static void SeedMailboxCapacityPage(ShellViewModel shell)
    {
        var page = shell.Page<MailboxCapacityViewModel>(); page.Refresh();
        page.MailboxMode = page.LicenceMode = "Supplied file";
        page.MailboxFile = MailboxCapacityFixtureFile; page.LicenceFile = MailboxCapacityLicenceFile;
        page.Search = ""; page.EntitlementFilter = "All"; page.Review(); page.SelectedRow = page.Rows[0];
    }
    private static void CheckMailboxCapacityNativeFlows(ShellViewModel shell, Window window, FrameworkElement content, string output, BindingTrace traces)
    {
        var snapshot = shell.Workspace.Snapshot; var plan = shell.Workspace.Plan;
        var mailboxBytes = File.ReadAllBytes(MailboxCapacityFixtureFile); var licenceBytes = File.ReadAllBytes(MailboxCapacityLicenceFile);
        foreach (var size in PageSizes)
        {
            traces.Context = "native mailbox capacity " + size;
            shell.Navigate("mailbox-capacity"); FitNativeReportClientSize(window, content, size);
            var navigation = shell.Page<MailboxCapacityViewModel>(); navigation.SelectedTab = 0;
            content.UpdateLayout(); Pump();
            CaptureReportState(content, output, "mailbox-capacity-source-path", size);
            var run = shell.Page<RunReportsViewModel>();
            var enabled = run.ExperimentalReadEnabled; var review = run.HasReview; var approved = run.Approved;
            PressOfflineReportCommand(shell, navigation.OpenMailboxCollectionCommand, "open mailbox collection page only");
            if (!ReferenceEquals(shell.CurrentPage, run) || run.ExperimentalReadEnabled != enabled || run.HasReview != review || run.Approved != approved)
                throw new InvalidOperationException("Mailbox collection navigation changed Run authorisation or failed to open its page.");
            PressOfflineReportCommand(shell, run.OpenMailboxCapacityCommand, "return to mailbox capacity without collection");
            if (!ReferenceEquals(shell.CurrentPage, navigation)) throw new InvalidOperationException("Run did not return to mailbox review.");
            PressOfflineReportCommand(shell, navigation.OpenLicenceCollectionCommand, "open licence collection page only");
            if (shell.CurrentPage is not GraphReportsViewModel) throw new InvalidOperationException("Licence collection shortcut opened the wrong page.");
            shell.Navigate("mailbox-capacity"); SeedMailboxCapacityPage(shell); FitNativeReportClientSize(window, content, size);
            var page = shell.Page<MailboxCapacityViewModel>(); var where = "native mailbox capacity " + size;
            if (page.Rows.Count != 2 || !page.Summary.Contains("Partial", StringComparison.Ordinal) || !page.Rows.Any(row => row.Configured == "Yes" && row.Entitlement == "Eligible")
                || !page.Rows.Any(row => row.Configured == "Unknown" && row.Entitlement == "Unable to check"))
                throw new InvalidOperationException("Mailbox capacity lost exact eligibility or Unlimited/unknown presentation.");
            AssertNewPageRowViewport(content, "Mailbox capacity results", where); CheckIdentityHeader(shell, content, where);
            var table = Descendants(content).OfType<DataGrid>().Single(grid => System.Windows.Automation.AutomationProperties.GetName(grid) == "Mailbox capacity results");
            var address = page.Rows[0].Smtp;
            if (!Descendants(table).OfType<TextBlock>().Any(text => text.Text == address && IsShown(text, content) && text.ToolTip is string tooltip && tooltip == address))
                throw new InvalidOperationException("Rendered mailbox SMTP cell lost its nonblank original value or full-value tooltip.");
            CaptureReportState(content, output, "mailbox-capacity-results", size);
            page.SelectedRow = page.Rows[1];
            var tabs = Descendants(content).OfType<TabControl>().Single(t => t.Items.OfType<TabItem>().Any(i => Equals(i.Header, "Mailboxes")));
            tabs.SelectedIndex = 1; content.UpdateLayout(); Pump();
            CheckExactDetail(content, "Mailbox identities raw quotas and archive details", page.SelectedDetails);
            if (!page.SelectedDetails.Contains("Unlimited", StringComparison.Ordinal) || !page.SelectedDetails.Contains("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", StringComparison.Ordinal))
                throw new InvalidOperationException("Mailbox details lost Unlimited or the exact original identity.");
            CaptureReportState(content, output, "mailbox-capacity-details", size); tabs.SelectedIndex = 0;
            page.Search = "Synthetic user"; content.UpdateLayout(); Pump();
            if (page.Rows.Count != 1 || !page.FilterSummary.Contains("1 of 2", StringComparison.Ordinal)) throw new InvalidOperationException("Mailbox display filter contract failed.");
            var before = Directory.GetFiles(shell.Workspace.Paths.ReportsDirectory).ToHashSet(StringComparer.Ordinal);
            PressOfflineReportCommand(shell, page.ExportJsonCommand, "export full derived mailbox capacity review");
            var file = Directory.GetFiles(shell.Workspace.Paths.ReportsDirectory).Single(path => !before.Contains(path));
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
            if (json["kind"]!.GetValue<string>() != "mailboxCapacityDerivedReview" || json["review"]!["mailboxes"]!.AsArray().Count != 2
                || !System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(System.Text.Encoding.UTF8.GetString(mailboxBytes)), json["originalMailboxSource"]))
                throw new InvalidOperationException("Filtered mailbox export lost full source evidence or derived-review labelling.");
            if (!System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(System.Text.Encoding.UTF8.GetString(licenceBytes)), json["originalLicenceSource"])) throw new InvalidOperationException("Filtered mailbox export changed the original licence evidence.");
            page.SelectedTab = 2; content.UpdateLayout(); Pump(); CheckExactDetail(content, "Mailbox and licence source timestamps digests and limitations", page.Provenance);
            CaptureReportState(content, output, "mailbox-capacity-provenance", size);
            page.MailboxMode = page.LicenceMode = "Saved record"; page.RefreshSources();
            page.SelectedMailbox = page.MailboxRecords.Single(e => e.Id == "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa8");
            page.SelectedLicence = page.LicenceRecords.Single(e => e.Id == "66666666-6666-4666-8666-666666666666");
            page.Review(); if (page.Rows.Count != 1) throw new InvalidOperationException("Saved review should retain the deliberate display filter.");
            page.Search = ""; if (page.Rows.Count != 2) throw new InvalidOperationException("Saved pair lost original mailbox rows.");
            page.MailboxFile = "different synthetic source.json";
            if (page.Rows.Count != 0 || page.LastExport.Length != 0 || page.ExportJsonCommand.CanExecute(null)) throw new InvalidOperationException("Source edit retained accepted export authority.");
            shell.ClearErrorCommand.Execute(null);
        }
        if (!ReferenceEquals(snapshot, shell.Workspace.Snapshot) || !ReferenceEquals(plan, shell.Workspace.Plan)
            || !File.ReadAllBytes(MailboxCapacityFixtureFile).SequenceEqual(mailboxBytes) || !File.ReadAllBytes(MailboxCapacityLicenceFile).SequenceEqual(licenceBytes))
            throw new InvalidOperationException("Mailbox review modified configuration/deployment state or original evidence bytes.");
        Console.WriteLine("Mailbox capacity native journeys completed at all three existing client sizes; no collector or token source used.");
    }
}
