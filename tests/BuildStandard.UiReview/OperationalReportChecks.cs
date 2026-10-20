using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
internal static partial class Program
{
    private static string SyntheticOperationalFile = "";
    private static void SeedOperationalEvidence(Workspace workspace, string output)
    {
        var stamp = Timestamps.Format(DateTimeOffset.UtcNow);
        var report = new OperationalContextReport
        {
            Id = Id(890), ReportId = "service-health", AccessIntent = "service-health", TenantId = Tenant,
            SourceMode = "historical", AsOfUtc = stamp, StartedAt = stamp, EndedAt = stamp, ToolkitVersion = workspace.Version,
            Status = ReportReadState.Partial, Sources = OperationalContextSchema.SourceDeclarations("service-health").ToList(),
            Sections = [new() { Id = "health-overviews", Status = ReportReadState.Partial, Error = "A synthetic future state is unclassified." },
                new() { Id = "health-issues", Status = ReportReadState.Failed, Error = "Synthetic access refusal; no empty-success result." }]
        };
        var rowOptions = new JsonSerializerOptions(BuildStandard.Core.Json.ToolkitJson.Options)
        { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never };
        foreach (var row in new[]
        {
            new ServiceHealthOverviewRow { Id = "SyntheticServiceA", Name = "Synthetic service A", Status = "serviceOperational", ReadStatus = ReportReadState.Collected },
            new ServiceHealthOverviewRow { Id = "SyntheticServiceB", Name = "Synthetic service B", Status = "futureStatus", ReadStatus = ReportReadState.Partial, Error = "Synthetic unknown state; never healthy." }
        }) report.Sections[0].Rows.Add(JsonSerializer.SerializeToNode(row, rowOptions)!.AsObject());
        OperationalContextSchema.Seal(report);
        SyntheticOperationalFile = Path.Combine(workspace.Paths.ReportsDirectory, "synthetic-operational-source.json");
        File.WriteAllText(SyntheticOperationalFile, "\uFEFF  " + OperationalContextSchema.Serialise(report) + "\n", new UTF8Encoding(false));
        File.Copy(SyntheticOperationalFile, Path.Combine(output, "synthetic-operational-source.json"));
    }
    private static void SeedOperationalPage(ShellViewModel shell)
    {
        var parent = shell.Page<GraphReportsViewModel>(); parent.SelectedTab = 4;
        AwaitReader(parent.Operational.OpenSuppliedAsync(SyntheticOperationalFile));
    }
    private static void CheckOperationalNativeFlows(ShellViewModel shell, Window window, FrameworkElement content, string output)
    {
        foreach (var size in new[] { new Size(1180,640), new Size(1180,760), new Size(1480,940) })
        {
            shell.Navigate("reports"); SeedOperationalPage(shell); FitNativeReportClientSize(window, content, size);
            var vm = shell.Page<GraphReportsViewModel>().Operational;
            var tabs = Descendants(content).OfType<TabControl>().Single(tab => tab.Items.OfType<TabItem>().Any(item => Equals(item.Header, "Read context")));
            tabs.SelectedIndex = 1; vm.SelectedSection = "health-overviews"; content.UpdateLayout(); Pump();
            if (vm.Rows?.Count != 2 || !vm.Outcome.Contains("Partial", StringComparison.Ordinal) || !vm.Notice.Contains("not proof", StringComparison.Ordinal))
                throw new InvalidOperationException("Operational native visibility lost partial/unknown/failed source meaning.");
            var grid = Descendants(content).OfType<DataGrid>().Single(control => AutomationProperties.GetName(control) == "Operational observations and source provenance");
            grid.BringIntoView(); content.UpdateLayout(); Pump();
            AssertNewPageRowViewport(content, "Operational observations and source provenance", "operational context " + size);
            CaptureReportState(content, output, "operational-results-partial", size, checkPageScrolling: false);
            vm.Search = "not present"; if (vm.Rows?.Count != 0) throw new InvalidOperationException("Operational display filtering failed.");
            foreach (var command in new[] { vm.HtmlCommand, vm.JsonCommand, vm.CsvCommand, vm.ExcelCommand }) PressOfflineReportCommand(shell, command, "Operational complete export");
            vm.Search = ""; vm.SelectedSection = vm.Sections[0]; content.UpdateLayout(); Pump();
            CaptureReportState(content, output, "operational-source-provenance", size, checkPageScrolling: false);
            tabs.SelectedIndex = 0; content.UpdateLayout(); Pump();
            if (vm.ReadCommand.CanExecute(null)) throw new InvalidOperationException("Unacknowledged synthetic operational view permitted Read.");
            CaptureReportState(content, output, "operational-deliberate-read", size, checkPageScrolling: false);
            tabs.SelectedIndex = 2; content.UpdateLayout(); Pump(); CaptureReportState(content, output, "operational-original-recovery", size, checkPageScrolling: false);
        }
    }
}
