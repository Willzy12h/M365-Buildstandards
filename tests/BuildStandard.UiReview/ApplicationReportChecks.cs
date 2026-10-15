using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
using BuildStandard.Tests;

internal static partial class Program
{
    private static readonly Dictionary<string, string> SyntheticApplicationReportFiles = new(StringComparer.Ordinal);
    private static void SeedApplicationReportEvidence(Workspace workspace)
    {
        SyntheticApplicationReportFiles.Add("expiry", workspace.Evidence.SaveApplicationReport(ApplicationReportUiFixtures.Expiry(Tenant, Operator, workspace.Version)));
        SyntheticApplicationReportFiles.Add("consent", workspace.Evidence.SaveApplicationReport(ApplicationReportUiFixtures.Consent(Tenant, Operator, workspace.Version)));
        SyntheticApplicationReportFiles.Add("large", workspace.Evidence.SaveApplicationReport(ApplicationReportUiFixtures.Expiry(Tenant, Operator, workspace.Version, 120)));
    }

    // Called inside the existing three-size report journey. These are offline strict fixtures, never a collector.
    private static void CheckApplicationReportNativeFlows(ShellViewModel shell, FrameworkElement content, string output, Size size, BindingTrace traces)
    {
        shell.Navigate("reports"); var vm = shell.Page<GraphReportsViewModel>();
        foreach (var family in new[] { "expiry", "consent" })
        {
            traces.Context = "native application " + family + " " + size;
            var path = SyntheticApplicationReportFiles[family]; var before = File.ReadAllBytes(path);
            var report = ReportEnvelopeDispatcher.ReadFile(path, Tenant).Schema2!;
            vm.SelectedReport = GraphReportPresentation.Find(2, report.ReportId);
            vm.Search = ""; vm.RowState = "All"; vm.HorizonDays = "30"; vm.RefreshStored();
            vm.SelectedStored = vm.StoredReports.Single(e => e.Id == report.Id);
            PressOfflineReportCommand(shell, vm.OpenStoredCommand, "open stored application " + family);
            if (vm.SelectedTab != 1 || !vm.Details.Contains(report.IntegrityDigest, StringComparison.Ordinal)
                || !vm.Details.Contains(ApplicationReportUiFixtures.At, StringComparison.Ordinal))
                throw new InvalidOperationException("Stored application report lost its original provenance.");
            vm.OpenSupplied(path); content.UpdateLayout(); Pump();
            if (!vm.ProvenanceNotice.Contains(RegisteredReportDocuments.SuppliedFileNotice, StringComparison.Ordinal)
                || !vm.Details.Contains(family == "expiry" ? "Horizon days: 30" : "Not applicable", StringComparison.Ordinal))
                throw new InvalidOperationException("Supplied application report lost its fixed end, horizon or unverified-source notice.");
            var sheets = ApplicationReportDocuments.Sheets(report, true).Skip(1).ToArray();
            if (!vm.Sections.SequenceEqual(sheets.Select(s => s.Name))) throw new InvalidOperationException("Application child tables are not selectable.");
            foreach (var sheet in sheets)
            {
                ReportDetailsTabs(content).SelectedIndex = 0;
                vm.SelectedSection = sheet.Name; vm.RowState = "Failed";
                var hasStatus = sheet.Rows[0].Contains("Read status");
                if (!hasStatus && (vm.RowState != "All" || vm.CanFilterReadState || !vm.ReadStateFilterGuidance.Contains("parent/source", StringComparison.Ordinal)))
                    throw new InvalidOperationException("A child table without row status hid data behind the read-state filter.");
                vm.RowState = "All"; content.UpdateLayout(); Pump();
                if (vm.Rows!.Count != sheet.Rows.Count - 1) throw new InvalidOperationException("Application table omitted original rows.");
                if (vm.Rows.Count >= 2)
                {
                    AssertNewPageRowViewport(content, "Typed report rows", traces.Context + " " + sheet.Name);
                    vm.SelectedRow = vm.Rows[1];
                    ReportDetailsTabs(content).SelectedIndex = 1; content.UpdateLayout(); Pump();
                    CheckExactDetail(content, "Selected report row identity and read details", vm.SelectedRowDetails);
                    if (!sheet.Rows[2].Where(s => s.Length > 0).All(s => vm.SelectedRowDetails.Contains(s, StringComparison.Ordinal)))
                        throw new InvalidOperationException("Selected application row lost a full value or unknown reason.");
                    ReportDetailsTabs(content).SelectedIndex = 0; content.UpdateLayout(); Pump();
                }
                else if (vm.SectionSummary.Contains("Checked successfully", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unattempted synthetic delegated grants claimed an empty successful check.");
                CaptureReportState(content, output, "application-" + family + "-" + sheet.Name.Replace(' ', '-'), size);
            }
            ReportDetailsTabs(content).SelectedIndex = 2; content.UpdateLayout(); Pump();
            CheckExactDetail(content, "Report provenance and source claims", vm.Details);
            CaptureReportState(content, output, "application-" + family + "-provenance", size);
            ReportDetailsTabs(content).SelectedIndex = 0;
            vm.Search = "absent synthetic search value"; content.UpdateLayout(); Pump();
            if (vm.Rows!.Count != 0) throw new InvalidOperationException("Application visual search did not filter its table.");
            CheckApplicationExports(shell, report, family);
            if (!before.SequenceEqual(File.ReadAllBytes(path))) throw new InvalidOperationException("Application UI modified the saved record.");
            var session = shell.Workspace.Session!; var verified = session.TenantVerified;
            try
            {
                session.TenantVerified = false; NotifyFixture(shell.Workspace); vm.ReadAcknowledged = true;
                if (vm.ReadCommand.CanExecute(null)) throw new InvalidOperationException("Application Read enabled an unverified synthetic identity.");
                try { vm.ReadAsync().GetAwaiter().GetResult(); throw new InvalidOperationException("Application Read bypassed verified-context refusal."); }
                catch (ToolkitException) { }
            }
            finally { session.TenantVerified = verified; NotifyFixture(shell.Workspace); }
        }
        vm.Search = ""; vm.RowState = "All"; vm.OpenSupplied(SyntheticApplicationReportFiles["large"]);
        vm.SelectedSection = "applications"; ReportDetailsTabs(content).SelectedIndex = 0; content.UpdateLayout(); Pump();
        var table = Descendants(content).OfType<DataGrid>().Single(g => AutomationProperties.GetName(g) == "Typed report rows");
        var column = table.Columns.Single(c => Equals(c.Header, "Id"));
        var last = vm.Rows!.Count - 1;
        table.ScrollIntoView(vm.Rows[0], column); content.UpdateLayout(); Pump(); content.UpdateLayout(); Pump();
        if (!table.EnableRowVirtualization || table.ItemContainerGenerator.ContainerFromIndex(last) is not null)
            throw new InvalidOperationException("Application table no longer virtualises the synthetic large report.");
        var item = vm.Rows[last]; var expected = item["Id"].ToString()!;
        vm.SelectedRow = item; table.ScrollIntoView(item, column); content.UpdateLayout(); Pump(); content.UpdateLayout(); Pump();
        var row = table.ItemContainerGenerator.ContainerFromIndex(last) as DataGridRow ?? throw new InvalidOperationException("Application row failed to realise.");
        var cell = Descendants(row).OfType<DataGridCell>().Single(c => c.Column == column);
        var text = Descendants(cell).OfType<TextBlock>().Single();
        if (!IsShown(text, content) || text.Text != expected || text.ToolTip is not string tooltip || tooltip != expected
            || !vm.SelectedRowDetails.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Application virtualised ID or full-value tooltip is blank or truncated.");
        CaptureReportState(content, output, "application-large-full-id", size);
        Console.WriteLine("Application expiry/consent saved/supplied, nested tables, filters, exports and virtualised IDs checked at " + size + "; zero tenant calls.");
    }
    private static void CheckApplicationExports(ShellViewModel shell, ApplicationReportEvidence expected, string family)
    {
        var vm = shell.Page<GraphReportsViewModel>();
        foreach (var (format, command) in new[] { (ExportFormat.Html, vm.ExportHtmlCommand), (ExportFormat.Json, vm.ExportJsonCommand),
            (ExportFormat.Csv, vm.ExportCsvCommand), (ExportFormat.Xlsx, vm.ExportXlsxCommand) })
        {
            var before = Directory.GetFiles(shell.Workspace.Paths.ReportsDirectory).ToHashSet(StringComparer.Ordinal);
            PressOfflineReportCommand(shell, command, "full application report " + format);
            var path = Directory.GetFiles(shell.Workspace.Paths.ReportsDirectory).Single(p => !before.Contains(p));
            if (!vm.LastExport.Contains(path, StringComparison.Ordinal)) throw new InvalidOperationException("Application export path is not shown.");
            if (format == ExportFormat.Json)
            {
                var actual = ApplicationReportEvidenceSchema.Read(File.ReadAllText(path).TrimStart('\uFEFF'), Tenant);
                if (ApplicationReportEvidenceSchema.Serialise(actual) != ApplicationReportEvidenceSchema.Serialise(expected))
                    throw new InvalidOperationException("Filtered application JSON changed the original evidence.");
                continue;
            }
            string exported;
            if (format == ExportFormat.Html) exported = File.ReadAllText(path);
            else { using var archive = ZipFile.OpenRead(path); exported = string.Join("\n", archive.Entries.Select(e => { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); })); }
            if (!exported.Contains(family == "expiry" ? "Synthetic credential 1" : "Synthetic.Write", StringComparison.Ordinal)
                || !exported.Contains(RegisteredReportDocuments.SuppliedFileNotice, StringComparison.Ordinal))
                throw new InvalidOperationException("Filtered application export omitted original nested metadata or supplied provenance.");
        }
    }
}
