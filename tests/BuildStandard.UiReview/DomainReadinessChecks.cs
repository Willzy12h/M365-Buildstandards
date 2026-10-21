using System.IO;
using System.Reflection;
using System.Windows;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Exchange;

internal static partial class Program
{
    private static void CaptureDomainRecovery(ShellViewModel shell,FrameworkElement content,Size size,string output)
    {
        var pages=(IDictionary<string,PageViewModel>)typeof(ShellViewModel).GetField("_pages",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(shell)!;
        var original=pages["domain-readiness"];var calls=0;
        // Construct the existing internal test seam only in the harness. No DNS resolver is used.
        Func<DomainReadinessRequest,CancellationToken,Task<DomainReadinessReport>> collect=(request,_)=>
        {
            calls++;
            var report=DomainReadinessSchema.Read(File.ReadAllBytes(Path.Combine(shell.Workspace.Paths.ReportsDirectory,"synthetic-domain-report.json")));
            report.TenantId=request.TenantId;report.AccountObjectId=request.AccountObjectId;
            report.IntegrityDigest=EvidenceIntegrity.Compute(report);
            return Task.FromResult(report);
        };
        var controller=(DomainReadinessController)Activator.CreateInstance(typeof(DomainReadinessController),BindingFlags.NonPublic|BindingFlags.Instance,null,
            new object[]{shell.Workspace,collect},null)!;
        var page=(DomainReadinessViewModel)Activator.CreateInstance(typeof(DomainReadinessViewModel),BindingFlags.NonPublic|BindingFlags.Instance,null,
            new object[]{shell,controller},null)!;
        try
        {
            pages["domain-readiness"]=page;shell.Navigate("domain-readiness");
            page.Domain="example.invalid";page.CheckSpf=false;page.Acknowledged=true;AwaitReader(page.RunAsync());
            if(calls!=1||!page.HasPending||controller.Current is null||page.CanRun)throw new InvalidOperationException("Original domain read was not retained without permitting replacement.");
            var originalHash=controller.Pending!.OriginalSha256;
            page.Domain="changed.invalid";page.Domain="example.invalid";
            if(controller.Current is not null||!page.HasPending)throw new InvalidOperationException("A–B–A admitted a stale domain result or lost its original.");
            var guard=typeof(BuildStandard.App.MainWindow).GetMethod("ShowPendingReport",BindingFlags.NonPublic|BindingFlags.Static)!;
            if(!(bool)guard.Invoke(null,new object[]{shell})!||!shell.ErrorMessage.Contains("unsaved",StringComparison.Ordinal))
                throw new InvalidOperationException("Close did not route to original-domain recovery.");
            content.UpdateLayout();Pump();
            var label="domain original recovery "+size.Width+"x"+size.Height;
            RecordUnnamedControls(content,label);RecordLowContrast(content,label);RecordClipping(content,label);
            SaveImage(content,size,Path.Combine(output,$"domain-readiness-recovery-{(int)size.Width}x{(int)size.Height}.png"));
            if(page.RecoverCommand.CanExecute(null)||page.DiscardCommand.CanExecute(null))throw new InvalidOperationException("Recovery was enabled without acknowledgement.");
            page.RecoveryAcknowledged=true;
            if(!page.RecoverCommand.CanExecute(null)||!page.DiscardCommand.CanExecute(null))throw new InvalidOperationException("Acknowledged recovery has no available next action.");
            var save=controller.ExportAsync(shell.Workspace.Paths.ReportsDirectory,true,true);AwaitReader(save);page.Refresh();
            if(page.HasPending||calls!=1||CanonicalJson.Sha256Hex(File.ReadAllBytes(Path.Combine(save.Result,"report.json")))!=originalHash)
                throw new InvalidOperationException("Original-domain recovery changed bytes, lost pending status or repeated a read.");
            if((bool)guard.Invoke(null,new object[]{shell})!)throw new InvalidOperationException("Close stayed blocked after original recovery.");
        }
        finally { pages["domain-readiness"]=original;shell.ClearErrorCommand.Execute(null);shell.Navigate("domain-readiness");content.UpdateLayout();Pump(); }
    }
}
