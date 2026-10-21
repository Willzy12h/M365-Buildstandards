using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Reports;
internal static partial class Program
{
    private static string SyntheticExternalFile = "";
    private static void SeedExternalEvidence(Workspace workspace, string output)
    {
        var source = new JsonObject { ["kind"] = "external-check-results", ["schemaVersion"] = 1,
            ["toolName"] = "Synthetic external checks", ["toolVersion"] = "1.0", ["sourceRunId"] = "synthetic-offline-review", ["tenantId"] = Tenant,
            ["performedAt"] = "2026-10-20T09:00:00Z", ["sourceState"] = "Partial", ["integrityDigest"] = "",
            ["rows"] = new JsonArray(new JsonObject { ["testId"] = "TEST-1", ["outcome"] = "Passed", ["title"] = "Synthetic source-declared pass" },
                new JsonObject { ["testId"] = "TEST-2", ["outcome"] = "FutureOutcome", ["title"] = "Synthetic unclassified result", ["reason"] = "<script>inert text only</script>", ["reference"] = "javascript:never-open" }) };
        source["integrityDigest"] = CanonicalJson.Sha256(source);
        SyntheticExternalFile = Path.Combine(output, "synthetic-external-results.json"); File.WriteAllBytes(SyntheticExternalFile, Encoding.UTF8.GetBytes("  " + source.ToJsonString() + "\n"));
    }
    private static void SeedExternalPage(ShellViewModel shell)
    {
        var parent = shell.Page<GraphReportsViewModel>(); parent.SelectedTab = 5; var vm = parent.External;
        if (vm.HasPendingOriginal) AwaitReader(vm.ExportPendingOriginalAsync());
        if (vm.ReferenceCount > 0) AwaitReader(vm.ExportReferencesAsync());
        AwaitReader(vm.OpenSuppliedAsync(SyntheticExternalFile));
    }
    private static void SeedExternalReference(ShellViewModel shell)
    {
        SeedExternalPage(shell); var vm = shell.Page<GraphReportsViewModel>().External;
        vm.SelectedTestId = "TEST-1"; vm.SelectedControlId = shell.Workspace.Standard!.Controls[0].Id; vm.AddReference();
    }
    private static void CheckExternalNativeFlows(ShellViewModel shell, Window window, FrameworkElement content, string output)
    {
        foreach (var size in new[] { new Size(1180,640), new Size(1180,760), new Size(1480,940) })
        {
            shell.Navigate("reports"); SeedExternalPage(shell); FitNativeReportClientSize(window,content,size);
            var vm = shell.Page<GraphReportsViewModel>().External;
            var panel = Descendants(content).OfType<BuildStandard.App.Views.ExternalCheckReviewPanel>().Single();
            var tabs = Descendants(panel).OfType<TabControl>().Single(); tabs.SelectedIndex = 0; content.UpdateLayout(); Pump();
            if (!vm.Outcome.Contains("Untrusted", StringComparison.Ordinal) || !vm.HasPendingOriginal || !vm.Provenance.Contains("Partial", StringComparison.Ordinal)) throw new InvalidOperationException("External source claims or pending original are missing.");
            CaptureReportState(content,output,"external-source-claims",size,checkPageScrolling:false);
            tabs.SelectedIndex = 1; content.UpdateLayout(); Pump();
            var search = Descendants(panel).OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Search external claimed results");
            search.BringIntoView(); content.UpdateLayout(); Pump(); if (!search.IsTabStop || !search.Focus()) throw new InvalidOperationException("External search is not keyboard reachable.");
            vm.Search = "TEST-2"; content.UpdateLayout(); Pump(); if (vm.Rows.Count != 1 || vm.Rows[0].DisplayOutcome != "Unclassified") throw new InvalidOperationException("External filter changed unknown semantics.");
            foreach (var command in new[] { vm.HtmlCommand, vm.CsvCommand, vm.ExcelCommand, vm.JsonCommand }) PressOfflineReportCommand(shell,command,"External complete claims export");
            var source = ExternalCheckResultsSchema.ReadFile(vm.LastExport,Tenant);
            if (source.Evidence.Rows.Count != 2 || !source.GetOriginalBytes().SequenceEqual(File.ReadAllBytes(SyntheticExternalFile))) throw new InvalidOperationException("External export filtered or rewrote the original.");
            CaptureReportState(content,output,"external-filtered-claims",size,checkPageScrolling:false);
            tabs.SelectedIndex = 2; vm.SelectedTestId = "TEST-1"; vm.SelectedControlId = shell.Workspace.Standard!.Controls[0].Id; vm.AddReference();
            AwaitReader(vm.ExportReferencesAsync()); AwaitReader(vm.OpenReferencesAsync(vm.LastExport)); content.UpdateLayout(); Pump();
            if (vm.ReferenceCount != 1 || !vm.ReferenceSummary.Contains("1 resolved",StringComparison.Ordinal)) throw new InvalidOperationException("Exact external control references did not round-trip.");
            CaptureReportState(content,output,"external-control-references",size,checkPageScrolling:false);
        }
    }
}
