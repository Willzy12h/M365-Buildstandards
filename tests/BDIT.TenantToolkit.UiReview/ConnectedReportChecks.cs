using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.App.Views;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;

/// <summary>Native navigation and offline report journeys over immutable synthetic evidence. No collector or token provider.</summary>
internal static partial class Program
{
    private static string SyntheticPartialReport = "";
    private static readonly Dictionary<string, string> SyntheticReportFiles = new(StringComparer.Ordinal);
    private static int GraphReportViewportsMeasured;
    private static int NamingViewportsMeasured;
    private static int GraphReportReadRefusals;
    private static int GraphReportExportsChecked;

    private static void SeedGraphReportEvidence(Workspace workspace)
    {
        var definition = GraphReportRegistry.Find("intune-devices");
        var now = Timestamps.Format(DateTimeOffset.UtcNow);
        foreach (var (label, status, count, record) in new[]
        {
            ("collected", ReportReadState.Collected, 2, 600), ("partial", ReportReadState.Partial, 3, 601),
            ("failed", ReportReadState.Failed, 0, 602), ("zero", ReportReadState.Collected, 0, 603),
            ("large", ReportReadState.Collected, 120, 604)
        })
        {
            var section = new ReportSection { Id = "devices", Status = status,
                Error = status == ReportReadState.Partial ? "Synthetic incomplete ownership and last-sync fields; unknown values cannot establish compliance."
                    : status == ReportReadState.Failed ? "Synthetic denied read. No objects could be checked." : null };
            for (var i = 0; i < count; i++)
            {
                var partial = status == ReportReadState.Partial && i == count - 1;
                section.Rows.Add(ReportEvidenceSchema.Row(new DeviceReportRow
                {
                    Id = Id(2000 + i), Name = partial ? "Synthetic device with unknown fields" : "Synthetic managed device " + (i + 1).ToString("D3"),
                    UserId = i == 0 ? Operator : null, Ownership = partial ? null : "company", EnrolmentType = "windowsAzureADJoin",
                    ManagementAgent = "mdm", OperatingSystem = partial ? null : "Windows", OsVersion = partial ? null : "10.0.synthetic",
                    ComplianceState = partial ? null : "compliant", LastSyncAt = partial ? null : now,
                    ReadStatus = partial ? ReportReadState.Partial : ReportReadState.Collected,
                    Error = partial ? "Ownership, OS, compliance and last sync were not returned; each stays unknown." : null
                }));
            }
            var report = new ReportEvidence
            {
                Id = Id(record), ReportId = definition.Id, TenantId = Tenant, AccountObjectId = Operator,
                SourceMode = "historical", StartedAt = now, EndedAt = now, ToolkitVersion = workspace.Version,
                ModuleVersion = GraphReportRegistry.AdapterVersion, Status = status,
                Sources = definition.Routes.Select(r => new ReportSource { Api = "v1.0", RegisteredRoute = r.Path, Reference = definition.Reference }).ToList(),
                Limitations = ["OFFLINE SYNTHETIC UI FIXTURE. No Microsoft Graph or authentication calls occurred.", definition.Limitations,
                    "Unsigned supplied claims and digests do not authenticate the source or supply deployment before-evidence."],
                Sections = [section]
            };
            ReportEvidenceSchema.Seal(report);
            SyntheticReportFiles.Add(label, workspace.Evidence.SaveReport(report));
        }
        SyntheticPartialReport = SyntheticReportFiles["partial"];
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(SyntheticPartialReport)!, Id(605) + ".json"), "{}");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(SyntheticPartialReport)!, Id(606) + ".json"), "{ damaged synthetic evidence");
    }

    private static void SeedGraphReportsPage(ShellViewModel shell)
    {
        var vm = shell.Page<GraphReportsViewModel>(); vm.Refresh();
        vm.SelectedReport = GraphReportRegistry.Find("intune-devices"); vm.Search = ""; vm.RowState = "All";
        vm.SuppliedFile = SyntheticPartialReport; vm.OpenSupplied(SyntheticPartialReport);
        vm.SelectedRow = vm.Rows![0];
    }

    private static void PrepareStoredGraphReport(ShellViewModel shell)
    {
        SeedGraphReportsPage(shell);
        var vm = shell.Page<GraphReportsViewModel>(); vm.RefreshStored();
        vm.SelectedStored = vm.StoredReports.Single(e => e.Id == Id(601));
        vm.SelectedTab = 2;
    }

    private static void SeedNamingPage(ShellViewModel shell)
    {
        var vm = shell.Page<NamingAuditViewModel>(); vm.Refresh();
        vm.Search = ""; vm.OwnershipFilter = "All"; vm.CollectionFilter = "All";
        vm.Review(); vm.SelectedRow = vm.Rows.FirstOrDefault();
    }

    private static void CheckConnectedReportNativeFlows(ShellViewModel shell, Window window, FrameworkElement content, string output, BindingTrace traces)
    {
        var workspace = shell.Workspace;
        var originalSnapshot = workspace.Snapshot;
        var originalPlan = workspace.Plan;
        var originalAcknowledgement = workspace.AcknowledgedSnapshotId;
        var originalBytes = ToolkitJson.Serialize(workspace.Snapshot);
        var reportBytes = File.ReadAllBytes(SyntheticPartialReport);
        var session = workspace.Session ?? throw new InvalidOperationException("Reports native journey needs the existing synthetic session.");
        var verified = session.TenantVerified;
        try
        {
            foreach (var size in PageSizes)
            {
                session.TenantVerified = false; NotifyFixture(workspace);
                traces.Context = "native reports " + size;
                shell.Navigate("reports"); SeedGraphReportsPage(shell);
                FitNativeReportClientSize(window, content, size);
                Console.WriteLine($"Native reports/naming client content: {content.ActualWidth:0}x{content.ActualHeight:0}; outer window: {window.ActualWidth:0}x{window.ActualHeight:0}.");
                var vm = shell.Page<GraphReportsViewModel>();
                var where = "native reports " + (int)size.Width + "x" + (int)size.Height;
                if (vm.SelectedTab != 1 || vm.Rows?.Count != 3 || !vm.SectionSummary.Contains(ReportReadState.Partial, StringComparison.Ordinal))
                    throw new InvalidOperationException("Supplied partial report did not open into Results with its original rows and state.");
                AssertNewPageRowViewport(content, "Typed report rows", where);
                GraphReportViewportsMeasured++;
                CheckIdentityHeader(shell, content, where);
                CaptureReportState(content, output, "reports-supplied-partial", size);

                vm.SelectedRow = vm.Rows![2];
                var detailsTabs = ReportDetailsTabs(content);
                detailsTabs.SelectedIndex = 1; content.UpdateLayout(); Pump();
                CheckExactDetail(content, "Selected report row identity and read details", vm.SelectedRowDetails);
                if (!vm.SelectedRowDetails.Contains("Unknown / not returned", StringComparison.Ordinal))
                    throw new InvalidOperationException("Partial report fields lost their explicit unknown presentation.");
                CaptureReportState(content, output, "reports-unknown-row-details", size);
                detailsTabs.SelectedIndex = 2; content.UpdateLayout(); Pump();
                CheckExactDetail(content, "Report provenance and source claims", vm.Details);
                if (!Descendants(content).OfType<TextBlock>().Any(t => t.Text == RegisteredReportDocuments.SuppliedFileNotice && IsShown(t, content)))
                    throw new InvalidOperationException("Supplied-file provenance notice is not visible in the report details.");
                CaptureReportState(content, output, "reports-supplied-provenance", size);
                detailsTabs.SelectedIndex = 0; vm.Search = "Synthetic managed device 001"; content.UpdateLayout(); Pump();
                if (vm.Rows?.Count != 1 || !vm.FilterSummary.Contains("display filters only", StringComparison.Ordinal))
                    throw new InvalidOperationException("The report display filter or complete-export disclosure failed.");
                CaptureReportState(content, output, "reports-display-filter", size);
                CheckFullFilteredExports(shell, content);
                vm.Search = "";

                PrepareStoredGraphReport(shell); content.UpdateLayout(); Pump();
                CaptureReportState(content, output, "reports-saved-browser", size);
                foreach (var entry in vm.StoredReports.Where(e => e.Id == Id(605) || e.Id == Id(606)))
                {
                    vm.SelectedStored = entry; content.UpdateLayout(); Pump();
                    if (entry.Problem.Length == 0 || vm.OpenStoredCommand.CanExecute(null)
                        || !Descendants(content).OfType<TextBlock>().Any(t => t.Text == entry.Problem && IsShown(t, content)))
                        throw new InvalidOperationException("Damaged saved selection lost its visible refusal reason.");
                    try { vm.OpenStored(); throw new InvalidOperationException("A damaged saved entry opened."); }
                    catch (ToolkitException) { }
                }
                vm.SelectedStored = vm.StoredReports.Single(e => e.Id == Id(601));
                PressOfflineReportCommand(shell, vm.OpenStoredCommand, "open stored Graph report");
                if (vm.SelectedTab != 1 || vm.Rows?.Count != 3 || !vm.SectionSummary.Contains(ReportReadState.Partial, StringComparison.Ordinal))
                    throw new InvalidOperationException("Reopened stored partial evidence lost its original result state.");
                CaptureReportState(content, output, "reports-saved-partial", size);
                foreach (var label in new[] { "collected", "failed", "zero", "large" })
                {
                    vm.OpenSupplied(SyntheticReportFiles[label]); content.UpdateLayout(); Pump();
                    if (label == "failed" && !vm.SectionSummary.Contains("not an empty successful check", StringComparison.Ordinal))
                        throw new InvalidOperationException("Failed report was presented as empty success.");
                    if (label == "zero" && !vm.SectionSummary.Contains("Checked successfully; no objects returned", StringComparison.Ordinal))
                        throw new InvalidOperationException("Collected zero report was not distinct from failed or unknown.");
                    if (label == "large" && vm.Rows?.Count != 120) throw new InvalidOperationException("Large report rows were lost.");
                    CaptureReportState(content, output, "reports-" + label, size);
                }

                vm.SelectedTab = 0; vm.ReadAcknowledged = true; content.UpdateLayout(); Pump();
                if (vm.ReadCommand.CanExecute(null) || vm.StopReadCommand.CanExecute(null) || vm.ReadGuidance.Length == 0)
                    throw new InvalidOperationException("Unverified synthetic report context enabled a live read or lost its refusal guidance.");
                try { vm.ReadAsync().GetAwaiter().GetResult(); throw new InvalidOperationException("Direct report read accepted an unverified synthetic connection."); }
                catch (ToolkitException) { GraphReportReadRefusals++; }
                CaptureReportState(content, output, "reports-read-refusal", size);
                vm.ReadAcknowledged = false;
                session.TenantVerified = verified; NotifyFixture(workspace);

                traces.Context = "native naming " + size;
                shell.Navigate("naming"); SeedNamingPage(shell); content.UpdateLayout(); Pump();
                var naming = shell.Page<NamingAuditViewModel>();
                var expectedObjects = naming.Rows.Count;
                PressOfflineReportCommand(shell, naming.ReviewCommand, "repeat attached naming review");
                if (naming.CollectionFilter != "All" || naming.Rows.Count != expectedObjects || expectedObjects < 2)
                    throw new InvalidOperationException("Attached repeated naming review lost its valid All selection or original objects.");
                naming.SelectedRow = naming.Rows.First();
                AssertNewPageRowViewport(content, "Naming and toolkit ownership results", "native naming " + size);
                NamingViewportsMeasured++; CheckIdentityHeader(shell, content, "native naming " + size);
                CaptureReportState(content, output, "naming-objects", size);
                var namingTabs = Descendants(content).OfType<TabControl>().Single(t => t.Items.OfType<TabItem>().Any(i => Equals(i.Header, "Objects")));
                namingTabs.SelectedIndex = 1; content.UpdateLayout(); Pump();
                CheckExactDetail(content, "Naming object identity and ownership reason", naming.SelectedDetails);
                if (!naming.SelectedDetails.Contains("Object ID:", StringComparison.Ordinal) || !naming.SelectedDetails.Contains("Ownership:", StringComparison.Ordinal))
                    throw new InvalidOperationException("Naming details lost object identity or ownership reason.");
                CaptureReportState(content, output, "naming-object-details", size);
                namingTabs.SelectedIndex = 2; content.UpdateLayout(); Pump();
                CheckExactDetail(content, "Naming audit source identity and integrity", naming.Provenance);
                CaptureReportState(content, output, "naming-provenance", size);
                namingTabs.SelectedIndex = 0; content.UpdateLayout(); Pump();
                if (!naming.Provenance.Contains(originalSnapshot!.Id, StringComparison.Ordinal))
                    throw new InvalidOperationException("Naming audit did not use the exact existing harness snapshot.");
                CheckFailedNamingReview(shell, content, output, size);
            }
            if (!ReferenceEquals(workspace.Snapshot, originalSnapshot) || !ReferenceEquals(workspace.Plan, originalPlan)
                || workspace.AcknowledgedSnapshotId != originalAcknowledgement || ToolkitJson.Serialize(workspace.Snapshot) != originalBytes
                || !reportBytes.SequenceEqual(File.ReadAllBytes(SyntheticPartialReport)))
                throw new InvalidOperationException("Report/naming journeys changed configuration, planning, acknowledgement or original report bytes.");
        }
        finally
        {
            session.TenantVerified = verified; NotifyFixture(workspace);
            window.Width = 1480; window.Height = 940; content.UpdateLayout(); Pump();
        }
    }

    private static void FitNativeReportClientSize(Window window, FrameworkElement content, Size requested)
    {
        // A shown HWND resizes separately from layout. Settle before measuring the client delta,
        // then verify it again after another pass; PageSizes and the one-pixel assertion stay fixed.
        const int maximumAttempts = 8;
        void Settle()
        {
            window.UpdateLayout(); Pump();
            Thread.Sleep(10);
            content.UpdateLayout(); Pump(); window.UpdateLayout();
        }
        bool Matches() => Math.Abs(content.ActualWidth - requested.Width) <= 1
            && Math.Abs(content.ActualHeight - requested.Height) <= 1;
        string Dimensions(int attempt) => $"attempt {attempt}/{maximumAttempts}; requested client {requested.Width:0.###}x{requested.Height:0.###}; "
            + $"requested outer {window.Width:0.###}x{window.Height:0.###}; actual outer {window.ActualWidth:0.###}x{window.ActualHeight:0.###}; "
            + $"actual client {content.ActualWidth:0.###}x{content.ActualHeight:0.###}; "
            + $"measured chrome {window.ActualWidth - content.ActualWidth:0.###}x{window.ActualHeight - content.ActualHeight:0.###}; "
            + $"window min {window.MinWidth:0.###}x{window.MinHeight:0.###}, max {window.MaxWidth:0.###}x{window.MaxHeight:0.###}; "
            + $"content min {content.MinWidth:0.###}x{content.MinHeight:0.###}, max {content.MaxWidth:0.###}x{content.MaxHeight:0.###}; state {window.WindowState}.";
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            Settle();
            Console.WriteLine("Native report sizing: " + Dimensions(attempt));
            if (Matches())
            {
                Settle();
                if (Matches()) return;
            }
            if (attempt < maximumAttempts)
            {
                window.Width = Math.Max(window.MinWidth, window.ActualWidth + requested.Width - content.ActualWidth);
                window.Height = Math.Max(window.MinHeight, window.ActualHeight + requested.Height - content.ActualHeight);
            }
        }
        throw new InvalidOperationException("Native report journey client content size differs from the requested PageSizes measurement after bounded resize settlement. "
            + Dimensions(maximumAttempts));
    }

    private static void NotifyFixture(Workspace workspace) => typeof(Workspace).GetMethod("Notify", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(workspace, null);
    private static void CheckFailedNamingReview(ShellViewModel shell, FrameworkElement content, string output, Size size)
    {
        var vm = shell.Page<NamingAuditViewModel>();
        var file = Path.Combine(shell.Workspace.Paths.TenantDirectory(Tenant), "managed-objects.json");
        var original = File.Exists(file) ? File.ReadAllBytes(file) : null;
        try
        {
            File.WriteAllText(file, "{ damaged synthetic mapping");
            ClearError(shell); vm.ReviewCommand.Execute(null); content.UpdateLayout(); Pump();
            if (!shell.HasError || vm.Rows.Count != 0 || vm.Provenance != "No reviewed capture."
                || !Descendants(content).OfType<TextBlock>().Any(t => t.Text == vm.Summary && IsShown(t, content)))
                throw new InvalidOperationException("Failed naming review retained accepted results or hid its cleared summary.");
            var tabs = Descendants(content).OfType<TabControl>().Single(t => t.Items.OfType<TabItem>().Any(i => Equals(i.Header, "Objects")));
            tabs.SelectedIndex = 2; content.UpdateLayout(); Pump();
            CheckExactDetail(content, "Naming audit source identity and integrity", "No reviewed capture.");
            CaptureReportState(content, output, "naming-failed-review", size, checkPageScrolling: false);
            tabs.SelectedIndex = 0;
        }
        finally
        {
            if (original is null) File.Delete(file); else File.WriteAllBytes(file, original);
            ClearError(shell); SeedNamingPage(shell); content.UpdateLayout(); Pump();
        }
    }
    private static TabControl ReportDetailsTabs(FrameworkElement content) => Descendants(content).OfType<TabControl>()
        .Single(t => t.Items.OfType<TabItem>().Any(i => Equals(i.Header, "Rows")));

    private static void AssertNewPageRowViewport(FrameworkElement content, string name, string where)
    {
        var table = Descendants(content).OfType<DataGrid>().Single(g => System.Windows.Automation.AutomationProperties.GetName(g) == name);
        var rows = Descendants(table).OfType<DataGridRow>().ToArray();
        var fullyVisible = rows.Count(r => IsShown(r, content) && VisibleBounds(r, content).Height >= r.ActualHeight - 1);
        var headerHeight = Descendants(table).OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>().Select(h => h.ActualHeight).DefaultIfEmpty(0).Max();
        var geometry = $"table {table.ActualWidth:0.###}x{table.ActualHeight:0.###}, visible {VisibleBounds(table, content).Width:0.###}x{VisibleBounds(table, content).Height:0.###}, header {headerHeight:0.###}; "
            + string.Join("; ", rows.Select(r => $"row {r.GetIndex()} height {r.ActualHeight:0.###}, visible {VisibleBounds(r, content).Height:0.###}"));
        Console.WriteLine(where + ": " + geometry);
        if (table.Items.Count < 2 || fullyVisible < 2) throw new InvalidOperationException(where + ": at least two populated rows must be fully visible; observed " + fullyVisible + ". " + geometry);
    }

    private static void CheckIdentityHeader(ShellViewModel shell, FrameworkElement content, string where)
    {
        foreach (var value in new[] { shell.TenantTitle, shell.TenantSubtitle, shell.HeaderAccount, shell.HeaderTenantId, shell.ModeBadge })
            if (!Descendants(content).OfType<TextBlock>().Any(t => t.Text == value && IsShown(t, content)))
                throw new InvalidOperationException(where + ": persistent tenant/domain/account/access header is not visible: " + value);
    }

    private static void CheckExactDetail(FrameworkElement content, string name, string expected)
    {
        var text = Descendants(content).OfType<TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == name);
        if (!text.IsReadOnly || text.Text != expected || !IsShown(text, content)) throw new InvalidOperationException(name + " is not complete, visible and copyable.");
    }

    private static void CaptureReportState(FrameworkElement content, string output, string label, Size size, bool checkPageScrolling = true)
    {
        var where = label + " " + (int)size.Width + "x" + (int)size.Height;
        RecordUnnamedControls(content, where); RecordCollapsedColumns(content, where); RecordLowContrast(content, where); RecordClipping(content, where);
        // The failed-review banner consumes extra page height; normal task states retain the standard no-scroll check.
        if (checkPageScrolling && size.Height >= 760) RecordUnneededScrolling(content, where);
        SaveWindowImage(content, Path.Combine(output, label + "-" + (int)size.Width + "x" + (int)size.Height + ".png"));
    }

    private static void PressOfflineReportCommand(ShellViewModel shell, ICommand command, string label)
    {
        if (!command.CanExecute(null)) throw new InvalidOperationException(label + " is disabled for the populated synthetic evidence.");
        ClearError(shell); command.Execute(null);
        var clock = Stopwatch.StartNew();
        while ((command as AsyncCommand)?.IsRunning == true || shell.Workspace.Busy)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(30)) throw new InvalidOperationException(label + " exceeded its offline harness deadline.");
            Pump(); Thread.Sleep(10);
        }
        Pump();
        if (shell.ErrorMessage.Length != 0) throw new InvalidOperationException(label + " failed: " + shell.ErrorMessage);
    }

    private static void CheckFullFilteredExports(ShellViewModel shell, FrameworkElement content)
    {
        var vm = shell.Page<GraphReportsViewModel>();
        var expected = ReportEvidenceSchema.Read(File.ReadAllText(SyntheticPartialReport), Tenant);
        foreach (var (format, command) in new[]
        {
            (ExportFormat.Html, vm.ExportHtmlCommand), (ExportFormat.Json, vm.ExportJsonCommand),
            (ExportFormat.Csv, vm.ExportCsvCommand), (ExportFormat.Xlsx, vm.ExportXlsxCommand)
        })
        {
            var before = Directory.GetFiles(shell.Workspace.Paths.ReportsDirectory).ToHashSet(StringComparer.Ordinal);
            PressOfflineReportCommand(shell, command, "complete filtered report " + format);
            var path = Directory.GetFiles(shell.Workspace.Paths.ReportsDirectory).Single(p => !before.Contains(p));
            if (!vm.LastExport.Contains(path, StringComparison.Ordinal)) throw new InvalidOperationException("Report export output path was not shown.");
            string exported;
            if (format == ExportFormat.Json)
            {
                var actual = ReportEvidenceSchema.Read(File.ReadAllText(path).TrimStart('\uFEFF'), Tenant);
                if (ReportEvidenceSchema.Serialize(actual) != ReportEvidenceSchema.Serialize(expected)) throw new InvalidOperationException("Filtered JSON export changed original evidence.");
                exported = "";
            }
            else if (format is ExportFormat.Csv or ExportFormat.Xlsx)
            {
                using var archive = ZipFile.OpenRead(path);
                exported = string.Join("\n", archive.Entries.Select(e => { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); }));
            }
            else exported = File.ReadAllText(path);
            if (format != ExportFormat.Json && (!exported.Contains("Synthetic device with unknown fields", StringComparison.Ordinal)
                || !exported.Contains("Synthetic managed device 002", StringComparison.Ordinal)
                || !exported.Contains(RegisteredReportDocuments.SuppliedFileNotice, StringComparison.Ordinal)
                || !exported.Contains(expected.IntegrityDigest, StringComparison.Ordinal)))
                throw new InvalidOperationException("Filtered " + format + " export omitted original rows or supplied provenance.");
            if (vm.Rows?.Count != 1) throw new InvalidOperationException("Export altered the display filter.");
            GraphReportExportsChecked++;
            content.UpdateLayout(); Pump();
        }
    }
}
