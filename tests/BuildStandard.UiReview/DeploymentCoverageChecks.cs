using System.Diagnostics;
using System.IO;
using System.Reflection;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Engine.Reports;
using BuildStandard.Tests;
internal static partial class Program
{
    private static void PrepareCoverageImport(ShellViewModel shell)
    {
        var original=CoverageTestData.Original(Tenant,Operator,Id(9801),Id(9802),Id(9803),includeGroup:true);
        var file=Path.Combine(shell.Workspace.Paths.ReportsDirectory,"synthetic-coverage-input.zip");File.WriteAllBytes(file,DeploymentCoverageBundle.Write(original));
        var page=shell.Page<DeploymentCoverageViewModel>();page.Refresh();page.ControlId=page.Controls.FirstOrDefault()?.Id??"";page.SourceFile=file;
    }
    private static void SeedCoveragePage(ShellViewModel shell)
    {
        PrepareCoverageImport(shell);var page=shell.Page<DeploymentCoverageViewModel>();WaitCoverage(page.ImportAsync());
        if(page.Rows?.Count!=5||!page.Outcome.Contains("Historical",StringComparison.Ordinal)||!page.OriginalReportDetails.Contains("CMP-HISTORY-001",StringComparison.Ordinal)||page.ControlId=="CMP-HISTORY-001")
            throw new InvalidOperationException("Coverage must show historical original A separately from prospective control B and keep source read states.");
        page.SelectedTable="Pilot group observations";
        if(page.Rows is null||!page.Rows.Cast<System.Data.DataRowView>().Any(row=>row.Row.ItemArray.Contains("Observed present")))
            throw new InvalidOperationException("Native coverage lost the interpreted historical pilot observation.");
        page.SelectedTable="Source read states";
        if(page.ReadCommand.CanExecute(null)||page.StopCommand.CanExecute(null))throw new InvalidOperationException("Synthetic import enabled a tenant read or Stop.");
    }
    private static void SeedCoverageRetainedOriginal(ShellViewModel shell)
    {
        var page=shell.Page<DeploymentCoverageViewModel>();page.SourceFile="";
        var original=CoverageTestData.Original(Tenant,Operator,Id(9801),Id(9802),Id(9804));var raw=CoverageTestData.OversizeForUi(original);
        var controller=typeof(DeploymentCoverageViewModel).GetField("_controller",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page)!;
        var flow=(DeploymentCoverageWorkflow)typeof(DeploymentCoverageController).GetField("_workflow",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(controller)!;
        WaitCoverage(flow.ReadCaptureAsync(_=>Task.FromResult(raw),new GraphReportsController(shell.Workspace).ContextKey,Tenant,true,default));
        page.Refresh();page.RecoveryAcknowledged=true;
        if(!page.HasPending||flow.Pending is not null||page.SaveOriginalCommand.CanExecute(null)||page.ExportOriginalCommand.CanExecute(null)||!page.ExportFilesCommand.CanExecute(null))
            throw new InvalidOperationException("An over-cap original must remain pending with manual original-file recovery only; no larger report or silent discard.");
    }
    private static void RecoverCoverageRetainedOriginal(ShellViewModel shell)
    {
        var page=shell.Page<DeploymentCoverageViewModel>();var command=(BuildStandard.App.Infrastructure.AsyncCommand)page.ExportFilesCommand;
        command.Execute(null);var watch=Stopwatch.StartNew();while(shell.Workspace.Busy){if(watch.Elapsed>TimeSpan.FromSeconds(20))throw new InvalidOperationException("Synthetic recovery did not finish.");Pump();}
        if(page.HasPending)throw new InvalidOperationException("Verified original-file export did not resolve the synthetic original.");
    }
    private static void WaitCoverage(Task task)
    {var watch=Stopwatch.StartNew();while(!task.IsCompleted){if(watch.Elapsed>TimeSpan.FromSeconds(20))throw new InvalidOperationException("Synthetic coverage task did not finish.");Pump();}task.GetAwaiter().GetResult();}
}
