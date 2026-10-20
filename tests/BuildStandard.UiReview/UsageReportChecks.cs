using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
internal static partial class Program
{
    private static string SyntheticUsageFile="";
    private static void SeedUsageEvidence(Workspace workspace,string output)
    {
        var columns=UsageReportRegistry.Find("email-activity").Columns;
        static string Quote(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
        var csv=Encoding.UTF8.GetBytes(string.Join(',',columns)+"\r\n"+string.Join(',',columns.Select(name=>Quote(name=="Report Refresh Date"?"2026-10-20":name=="Display Name"?"Synthetic engineer":name=="User Principal Name"?"unverified@synthetic.invalid":"")))+"\r\n");
        var now=Timestamps.Format(DateTimeOffset.UtcNow);
        var report=new UsageReviewReport{Id=Id(880),TenantId=Tenant,StartedAt=now,EndedAt=now,ToolkitVersion=workspace.Version,Status="Partial",
            Sources=[new(){SourceId="email-activity",Period="D30",Status="Collected",OriginalCsvBase64=Convert.ToBase64String(csv),CsvSha256=Convert.ToHexStringLower(SHA256.HashData(csv))},new(){SourceId="teams-activity",Period="D30",Status="Failed",Reason="Synthetic access refusal; not a successful empty result."}]};
        UsageReviewSchema.Seal(report);SyntheticUsageFile=Path.Combine(workspace.Paths.ReportsDirectory,"synthetic-usage-source.json");
        Directory.CreateDirectory(workspace.Paths.ReportsDirectory);File.WriteAllText(SyntheticUsageFile,UsageReviewSchema.Serialise(report),new UTF8Encoding(false));
        File.Copy(SyntheticUsageFile,Path.Combine(output,"synthetic-usage-source.json"));
    }
    private static void SeedUsagePage(ShellViewModel shell)
    {
        var parent=shell.Page<GraphReportsViewModel>();parent.SelectedTab=3;
        AwaitReader(parent.Usage.OpenSuppliedAsync(SyntheticUsageFile));
    }
    private static void CheckUsageNativeFlows(ShellViewModel shell,Window window,FrameworkElement content,string output)
    {
        foreach(var size in new[]{new Size(1180,640),new Size(1180,760),new Size(1480,940)})
        {
            shell.Navigate("reports");SeedUsagePage(shell);FitNativeReportClientSize(window,content,size);var vm=shell.Page<GraphReportsViewModel>().Usage;
            var tabs=Descendants(content).OfType<TabControl>().Single(t=>t.Items.OfType<TabItem>().Any(item=>Equals(item.Header,"Read usage")));
            tabs.SelectedIndex=1;vm.SelectedSection=UsageReportRegistry.Find("email-activity").Name;content.UpdateLayout();Pump();
            if(vm.Rows?.Count!=1||!vm.Outcome.Contains("Partial",StringComparison.Ordinal)||!vm.PrivacyNotice.Contains("Unknown",StringComparison.Ordinal))
                throw new InvalidOperationException("Usage native view lost partial source status, original row or unknown privacy.");
            var grid=Descendants(content).OfType<DataGrid>().Single(g=>AutomationProperties.GetName(g)=="Usage observations and source provenance");
            grid.BringIntoView();content.UpdateLayout();Pump();
            AssertNewPageRowViewport(content,"Usage observations and source provenance","usage results "+size);
            CaptureReportState(content,output,"usage-results-partial",size,checkPageScrolling:false);
            vm.Search="not present";if(vm.Rows?.Count!=0)throw new InvalidOperationException("Usage display filter failed.");
            foreach(var command in new[]{vm.HtmlCommand,vm.JsonCommand,vm.CsvCommand,vm.ExcelCommand})PressOfflineReportCommand(shell,command,"Usage complete export");
            vm.Search="";vm.SelectedSection=vm.Sections[0];content.UpdateLayout();Pump();CaptureReportState(content,output,"usage-source-provenance",size,checkPageScrolling:false);
            tabs.SelectedIndex=0;content.UpdateLayout();Pump();
            if(vm.ReadCommand.CanExecute(null))throw new InvalidOperationException("Unacknowledged synthetic usage view permitted Read.");
            CaptureReportState(content,output,"usage-deliberate-read",size,checkPageScrolling:false);
            tabs.SelectedIndex=2;content.UpdateLayout();Pump();CaptureReportState(content,output,"usage-original-recovery",size,checkPageScrolling:false);
        }
    }
}
